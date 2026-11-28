using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class ReportingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid BranchId { get; set; }
    public Guid Actor { get; set; }
    public Guid? BatchId { get; set; }
    public int? ConnectorVersion { get; set; }
    public required string Mode { get; set; }
    public required string Scenario { get; set; }
    public required string SchemaVersion { get; set; }
    public string? MappingVersion { get; set; }
    public string? SourceHash { get; set; }
    public string? PayloadHash { get; set; }
    public string State { get; set; } = "Queued";
    public string? Failure { get; set; }
    public bool VerifyOnly { get; set; }
    public int Attempts { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ReportingOperationAttempt
{
    public Guid OperationId { get; set; }
    public int Sequence { get; set; }
    public required string Action { get; set; }
    public required string Outcome { get; set; }
    public required string ResponseHash { get; set; }
    public string? Reference { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal static class ReportingOperations
{
    internal sealed record Request(Guid[] Documents, string Mode, string? Scenario, int? ConnectorVersion);
    internal sealed record Note(string Reason);
    internal static void Configure(ModelBuilder model)
    {
        model.Entity<ReportingOperation>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId, x.Mode, x.ConnectorVersion })
                .IsUnique().AreNullsDistinct(false);
            e.HasIndex(x => new { x.State, x.AvailableAt });
            e.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<RegulatoryConnectorVersion>().WithMany().HasForeignKey(x => x.ConnectorVersion).OnDelete(DeleteBehavior.Restrict);
        });
        model.Entity<ReportingOperationAttempt>(e =>
        {
            e.HasKey(x => new { x.OperationId, x.Sequence });
            e.HasOne<ReportingOperation>().WithMany().HasForeignKey(x => x.OperationId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    internal static void MapReportingOperations(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/reporting/operations")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect);
        group.MapGet("/", Register);
        group.MapGet("/{id:guid}/attempts", Attempts);
        group.MapPost("/", Queue);
        group.MapPost("/{id:guid}/cancel", (Guid subscriber, Guid company, Guid id, Note input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            Update(subscriber, company, id, input, false, http, db, ct));
        group.MapPost("/{id:guid}/verify", (Guid subscriber, Guid company, Guid id, Note input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
            Update(subscriber, company, id, input, true, http, db, ct));
    }

    private static IQueryable<ReportingOperation> Scoped(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.Set<ReportingOperation>().Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    private static async Task<IResult> Register(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db,
        IWebHostEnvironment env, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct, existingRecords: true) is not { } access) return TenantAccess.Missing();
        var rows = await Scoped(db, subscriber, company).AsNoTracking().OrderByDescending(x => x.At).Take(100).ToArrayAsync(ct);
        var canSimulate = env.IsDevelopment() && access.Grant.Subscriber.DevelopmentFixture && access.Grant.Grants.Contains(Permissions.CompanyManage);
        var active = canSimulate ? await RegulatoryConnector.LatestEvents(db).Where(x => x.State == "Active")
            .Select(x => (int?)x.Version).SingleOrDefaultAsync(ct) : null;
        return Results.Ok(new { items = rows, activeConnector = active, scenarios = canSimulate ? RegulatoryConnector.Scenarios : [],
            canPrepare = access.Grant.Grants.Contains(Permissions.CompanyManage), governmentStatus = "NotSubmitted" });
    }

    private static async Task<IResult> Attempts(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await DocumentEndpoints.Access(subscriber, company, "Reports.View", http, db, ct, existingRecords: true) is null ||
            !await Scoped(db, subscriber, company).AnyAsync(x => x.Id == id, ct)) return TenantAccess.Missing();
        var rows = await db.Set<ReportingOperationAttempt>().AsNoTracking().Where(x => x.OperationId == id)
            .OrderBy(x => x.Sequence).ToArrayAsync(ct);
        return Results.Ok(new { items = rows, governmentStatus = "NotSubmitted" });
    }

    private static async Task<IResult> Queue(Guid subscriber, Guid company, Request input, HttpContext http, EfiscalDbContext db,
        IWebHostEnvironment env, CancellationToken ct)
    {
        if (input.Documents is not { Length: >= 1 and <= 20 } || input.Documents.Distinct().Count() != input.Documents.Length ||
            input.Mode is not ("Preparation" or "DevelopmentSimulation"))
            return ApiSupport.Invalid("operation", "Select up to 20 distinct documents and a supported operating mode.");
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct, existingRecords: input.Mode == "Preparation") is not { } access ||
            !access.Grant.Grants.Contains("Reports.View")) return TenantAccess.Missing();
        var simulation = input.Mode == "DevelopmentSimulation";
        if (simulation && (!env.IsDevelopment() || !access.Grant.Subscriber.DevelopmentFixture)) return TenantAccess.Missing();
        var scenario = input.Scenario ?? "Accepted";
        if (simulation && !RegulatoryConnector.Scenarios.Contains(scenario))
            return ApiSupport.Invalid("scenario", "Choose a fictional simulator outcome.");
        var docs = await db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            input.Documents.Contains(x.Id) && (x.Type == "SalesInvoice" || x.Type == "CreditMemo") && (!simulation || x.DevelopmentFixture))
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.BranchId }).ToArrayAsync(ct);
        if (docs.Length != input.Documents.Length) return TenantAccess.Missing();
        var version = simulation ? input.ConnectorVersion : null;
        RegulatoryConnectorVersion? connector = null;
        if (simulation)
        {
            connector = await db.Set<RegulatoryConnectorVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Number == version &&
                x.Environment == "DevelopmentSimulation" && RegulatoryConnector.LatestEvents(db).Any(y => y.Version == x.Number && y.State == "Active"), ct);
            if (connector is null || (docs.Length > 1 || scenario == "PartialBatch") && !connector.Batch ||
                scenario == "PartialBatch" && docs.Length < 2)
                return ApiSupport.Problem(409, "Select an active synthetic connector with the required batch capability.");
        }
        var existing = await Scoped(db, subscriber, company).Where(x => input.Documents.Contains(x.DocumentId) &&
            x.Mode == input.Mode && x.ConnectorVersion == version).ToDictionaryAsync(x => x.DocumentId, ct);
        var batch = docs.Length > 1 ? Guid.NewGuid() : (Guid?)null;
        var ids = new List<Guid>(docs.Length);
        for (var i = 0; i < docs.Length; i++)
        {
            var doc = docs[i];
            var chosen = !simulation ? "" : scenario == "PartialBatch" ? i % 2 == 0 ? "Accepted" : "ValidationRejected" : scenario;
            if (existing.TryGetValue(doc.Id, out var previous))
            {
                if (previous.Scenario != chosen) return ApiSupport.Problem(409, "This document already has an operation for this version.");
                ids.Add(previous.Id);
                continue;
            }
            var canonical = simulation ? await db.ReportingPreparations.AsNoTracking().SingleOrDefaultAsync(x =>
                x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == doc.Id && x.SchemaVersion == CanonicalInvoices.Version, ct) : null;
            if (simulation && (canonical is null || !Integrity(canonical)))
                return ApiSupport.Problem(409, "An intact canonical record is required for the simulator.");
            var row = new ReportingOperation { SubscriberId = subscriber, CompanyId = company, DocumentId = doc.Id,
                BranchId = doc.BranchId, Actor = http.UserId(), BatchId = batch, ConnectorVersion = connector?.Number, Mode = input.Mode,
                Scenario = chosen, SchemaVersion = simulation ? CanonicalInvoices.Version : "embm-preparation-v1",
                MappingVersion = simulation ? RegulatoryConnector.MappingVersion : null, SourceHash = canonical?.Sha256,
                PayloadHash = canonical is null ? null : DocumentRules.Digest(RegulatoryConnector.Payload(JsonSerializer.Deserialize<JsonElement>(canonical.Content))) };
            db.Add(row);
            db.Audit("Reporting.OperationQueued", row.Actor, subscriber, row.Id);
            ids.Add(row.Id);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Accepted(value: new { operations = ids, governmentStatus = "NotSubmitted" });
    }

    internal static bool Integrity(ReportingPreparation row) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(row.Content))) == row.Sha256;

    private static async Task<IResult> Update(Guid subscriber, Guid company, Guid id, Note input, bool verify, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is not { } access ||
            !access.Grant.Grants.Contains("Reports.View")) return TenantAccess.Missing();
        if (!ApiSupport.Text(input.Reason, 600)) return ApiSupport.Invalid("reason", "Record a reason.");
        var row = await Scoped(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        if (verify)
        {
            if (row.State is not ("UncertainOutcome" or "RequiresReview") || row.Mode != "DevelopmentSimulation" ||
                !await db.Set<ReportingOperationAttempt>().AnyAsync(x => x.OperationId == id && x.Outcome == "UncertainOutcome", ct) ||
                await db.Set<ReportingOperationAttempt>().AnyAsync(x => x.OperationId == id && x.Action == "Verify", ct))
                return ApiSupport.Problem(409, "Only an unverified uncertain simulation may request status retrieval.");
            row.VerifyOnly = true;
            row.AvailableAt = DateTimeOffset.UtcNow;
            row.State = "Queued";
        }
        else
        {
            // An atomic predicate prevents cancelling a job claimed since the read.
            var updated = await Scoped(db, subscriber, company).Where(x => x.Id == id && x.State == "Queued" && x.Attempts == 0)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.State, "Cancelled").SetProperty(t => t.Failure, input.Reason.Trim()), ct);
            if (updated == 0) return ApiSupport.Problem(409, "Only unstarted jobs may be cancelled.");
        }
        db.Audit(verify ? "Reporting.StatusVerificationRequested" : "Reporting.OperationCancelled", http.UserId(), subscriber, id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }
}
