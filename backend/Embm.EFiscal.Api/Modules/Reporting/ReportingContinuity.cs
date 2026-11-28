using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class ContinuityRecord
{
    public Guid SubscriberId { get; init; }
    public Guid CompanyId { get; init; }
    public string Kind { get; init; } = "";
    public string Key { get; init; } = "";
    public int Version { get; init; }
    public Guid? BranchId { get; init; }
    public Guid ActorId { get; init; }
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.UtcNow;
    public JsonElement Details { get; init; }
}

internal static class ReportingContinuity
{
    private static readonly string[] Components = ["Reporting", "DocumentDelivery", "Accounting", "Storage"];
    private static readonly string[] IncidentStates = ["Investigating", "Recovering", "Closed"];
    internal sealed record IncidentInput(Guid? Id, Guid? BranchId, DateTimeOffset StartedAt, DateTimeOffset? RestoredAt,
        string[] Components, Guid[] ReportingOperationIds, string State, string Notes, string? EvidenceReference, int ExpectedVersion);
    internal sealed record DeadlineInput(string Obligation, DateOnly Commencement, int CalendarDays, int WarningDays,
        int RecoveryDays, int ExpectedVersion);
    internal sealed record ManualInvoiceInput(Guid IncidentId, Guid BranchId, string Series, string Number, DateOnly Date,
        string OriginalEvidenceSha256, Guid? ExistingDocumentId, string ReviewNotes);

    internal static void Configure(ModelBuilder model) => model.Entity<ContinuityRecord>(e =>
    {
        e.ToTable("ContinuityRecords");
        e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.Kind, x.Key, x.Version });
        e.Property(x => x.Kind).HasMaxLength(24);
        e.Property(x => x.Key).HasMaxLength(64);
        e.Property(x => x.Details).HasColumnType("jsonb");
        e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, Id = x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    });

    internal static void MapReportingContinuity(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/continuity")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Reporting continuity");
        group.MapGet("/", Register);
        group.MapPost("/incidents", RecordIncident);
        if (app.Environment.IsDevelopment())
        {
            group.MapPost("/manual-invoices", RecordManualReference);
            group.MapPost("/deadline-policy", RecordDeadlinePolicy);
            group.MapGet("/deadline-preview/{document:guid}", DeadlinePreview);
        }
    }

    private static IQueryable<ContinuityRecord> Scope(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<ContinuityRecord>().AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> Register(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct, existingRecords: true) is not { } access)
            return TenantAccess.Missing();
        var entries = await Scope(db, subscriber, company).OrderByDescending(x => x.RecordedAt).Take(150).ToArrayAsync(ct);
        return Results.Ok(new { entries, canManage = access.Grant.Grants.Contains(Permissions.CompanyManage),
            manualReferenceMode = "DevelopmentReviewOnly", regulatoryDeadline = "AwaitingVerifiedApplicability" });
    }

    private static async Task<IResult> RecordIncident(Guid subscriber, Guid company, IncidentInput input, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true) is null)
            return TenantAccess.Missing();
        if (input.StartedAt.Year < 2000 || input.StartedAt > DateTimeOffset.UtcNow ||
            input.RestoredAt < input.StartedAt || input.RestoredAt > DateTimeOffset.UtcNow ||
            input.State == "Closed" && input.RestoredAt is null || !IncidentStates.Contains(input.State) ||
            input.Components is not { Length: >= 1 and <= 4 } || input.Components.Distinct().Count() != input.Components.Length ||
            !input.Components.All(Components.Contains) || input.ReportingOperationIds is not { Length: <= 100 } ||
            input.ReportingOperationIds.Distinct().Count() != input.ReportingOperationIds.Length ||
            !ApiSupport.Text(input.Notes, 2000) || !ApiSupport.Optional(input.EvidenceReference, 500))
            return ApiSupport.Invalid("incident", "Record a valid period, affected components and investigation notes.");

        if (input.BranchId is Guid branch && !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == branch, ct))
            return TenantAccess.Missing();
        var inScope = await db.Set<ReportingOperation>().CountAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            (input.BranchId == null || x.BranchId == input.BranchId) && input.ReportingOperationIds.Contains(x.Id), ct);
        if (inScope != input.ReportingOperationIds.Length) return TenantAccess.Missing();

        var id = input.Id ?? Guid.NewGuid();
        var key = id.ToString("N");
        var latest = await Scope(db, subscriber, company).Where(x => x.Kind == "Incident" && x.Key == key)
            .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (input.Id.HasValue && latest is null) return TenantAccess.Missing();
        if (input.ExpectedVersion != (latest?.Version ?? 0)) return ApiSupport.Problem(409, "Reload the current incident revision.");
        if (latest is not null)
        {
            var prior = DocumentRules.Read<IncidentInput>(latest.Details);
            if (prior.State == "Closed" || prior.StartedAt != input.StartedAt || latest.BranchId != input.BranchId)
                return ApiSupport.Problem(409, "The original incident scope is fixed, and closed incidents are final.");
        }
        var row = new ContinuityRecord { SubscriberId = subscriber, CompanyId = company, Kind = "Incident", Key = key,
            Version = input.ExpectedVersion + 1, BranchId = input.BranchId, ActorId = http.UserId(),
            Details = DocumentRules.Freeze(input with { Id = id, StartedAt = input.StartedAt.ToUniversalTime(), RestoredAt = input.RestoredAt?.ToUniversalTime() }) };
        db.Add(row);
        db.Audit("Continuity.IncidentRecorded", row.ActorId, subscriber, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> RecordDeadlinePolicy(Guid subscriber, Guid company, DeadlineInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true)
            is not { Grant.IsOwner: true, Grant.Subscriber.DevelopmentFixture: true }) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Obligation, 120) || input.Commencement.Year is < 2000 or > 2199 ||
            input.CalendarDays is < 1 or > 366 || input.WarningDays < 1 || input.WarningDays > input.CalendarDays ||
            input.RecoveryDays is < 1 or > 366)
            return ApiSupport.Invalid("policy", "Set a fictional obligation, commencement date and valid day offsets.");
        var versions = Scope(db, subscriber, company).Where(x => x.Kind == "DeadlinePolicy");
        var version = await versions.MaxAsync(x => (int?)x.Version, ct) ?? 0;
        if (version != input.ExpectedVersion) return ApiSupport.Problem(409, "Reload the current fictional deadline policy.");
        var row = new ContinuityRecord { SubscriberId = subscriber, CompanyId = company, Kind = "DeadlinePolicy", Key = "simulation",
            Version = version + 1, ActorId = http.UserId(), Details = DocumentRules.Freeze(input) };
        db.Add(row);
        db.Audit("Continuity.FictionalDeadlinePolicyRecorded", row.ActorId, subscriber, company);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> DeadlinePreview(Guid subscriber, Guid company, Guid document, Guid? incident, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct, existingRecords: true)
            is not { Grant.Subscriber.DevelopmentFixture: true }) return TenantAccess.Missing();
        var source = await db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.Id == document && (x.Type == "SalesInvoice" || x.Type == "CreditMemo"))
            .Select(x => new { x.Date, x.BranchId }).SingleOrDefaultAsync(ct);
        if (source is null) return TenantAccess.Missing();
        var scope = Scope(db, subscriber, company);
        var version = (await scope.Where(x => x.Kind == "DeadlinePolicy").OrderByDescending(x => x.Version).Take(100).ToArrayAsync(ct))
            .FirstOrDefault(x => DocumentRules.Read<DeadlineInput>(x.Details).Commencement <= source.Date);
        if (version is null) return Results.Ok(new { mode = "DevelopmentSimulation", status = "NoApplicableFictionalPolicy",
            transactionDate = source.Date, legalOverdue = false });
        var policy = DocumentRules.Read<DeadlineInput>(version.Details);
        var deadline = source.Date.AddDays(policy.CalendarDays);
        DateTimeOffset? restoredAt = null;
        DateTimeOffset? startedAt = null;
        if (incident is Guid id)
        {
            var key = id.ToString("N");
            var outage = await scope.Where(x => x.Kind == "Incident" && x.Key == key && (x.BranchId == null || x.BranchId == source.BranchId))
                .OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
            if (outage is null) return TenantAccess.Missing();
            var details = DocumentRules.Read<IncidentInput>(outage.Details);
            restoredAt = details.RestoredAt;
            startedAt = details.StartedAt;
        }
        DateOnly? recoveryDeadline = restoredAt is null ? null : RegulatoryEligibility.ManilaDate(restoredAt.Value).AddDays(policy.RecoveryDays);
        var today = RegulatoryEligibility.ManilaDate(DateTimeOffset.UtcNow);
        var effective = recoveryDeadline ?? deadline;
        var status = today > effective ? "EscalateFictionalScenario" :
            effective.DayNumber - today.DayNumber <= policy.WarningDays ? "FictionalDeadlineApproaching" : "FictionalDeadlineFuture";
        return Results.Ok(new { mode = "DevelopmentSimulation", policyVersion = version.Version, policy.Obligation, policy.Commencement,
            transactionDate = source.Date, deadline, outageStartedAt = startedAt, restoredAt, recoveryDeadline, status,
            governmentSubmissionAt = (DateTimeOffset?)null, governmentAcknowledgmentAt = (DateTimeOffset?)null, legalOverdue = false });
    }

    private static async Task<IResult> RecordManualReference(Guid subscriber, Guid company, ManualInvoiceInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: true) is not
            { Grant.Subscriber.DevelopmentFixture: true }) return TenantAccess.Missing();
        if (input.Date.Year < 2000 || !ApiSupport.Text(input.Series, 40) || !ApiSupport.Text(input.Number, 60) ||
            input.OriginalEvidenceSha256 is not { Length: 64 } || !input.OriginalEvidenceSha256.All(char.IsAsciiHexDigit) ||
            !ApiSupport.Text(input.ReviewNotes, 2000))
            return ApiSupport.Invalid("manualInvoice", "Record an original series, number, date, evidence checksum and review notes.");

        var incidentKey = input.IncidentId.ToString("N");
        var incident = await Scope(db, subscriber, company).Where(x => x.Kind == "Incident" && x.Key == incidentKey &&
            (x.BranchId == null || x.BranchId == input.BranchId)).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        if (incident is null || !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == input.BranchId, ct) ||
            input.ExistingDocumentId is Guid id && !await db.FiscalDocuments.AnyAsync(x => x.SubscriberId == subscriber &&
                x.CompanyId == company && x.Id == id && x.BranchId == input.BranchId && x.Type == "SalesInvoice", ct))
            return TenantAccess.Missing();
        var outage = DocumentRules.Read<IncidentInput>(incident.Details);
        if (input.Date < RegulatoryEligibility.ManilaDate(outage.StartedAt) ||
            input.Date > RegulatoryEligibility.ManilaDate(outage.RestoredAt ?? DateTimeOffset.UtcNow))
            return ApiSupport.Invalid("date", "The original transaction must fall within the recorded outage period.");
        var series = input.Series.Trim().ToUpperInvariant();
        var number = input.Number.Trim().ToUpperInvariant();
        var key = DocumentRules.Digest(new { input.BranchId, series, number }).ToLowerInvariant();
        if (await Scope(db, subscriber, company).AnyAsync(x => x.Kind == "ManualInvoice" && x.Key == key, ct))
            return ApiSupport.Problem(409, "The original manual invoice has already been recorded.");
        var row = new ContinuityRecord { SubscriberId = subscriber, CompanyId = company, Kind = "ManualInvoice", Key = key,
            Version = 1, BranchId = input.BranchId, ActorId = http.UserId(),
            Details = DocumentRules.Freeze(new { original = input with { Series = series, Number = number }, eligibility = "RequiresVerifiedReview" }) };
        db.Add(row);
        db.Audit("Continuity.ManualReferenceRecorded", row.ActorId, subscriber, input.IncidentId);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Ok(row);
    }
}
