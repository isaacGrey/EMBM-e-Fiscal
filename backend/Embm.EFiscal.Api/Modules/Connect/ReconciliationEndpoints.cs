using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ActivateCoverageInput(bool Confirm, DateTimeOffset? EffectiveAt);
internal sealed record ReconciliationRequest(Guid? DocumentId, Guid? ConnectionId, string? Kind, DateOnly? From, DateOnly? Through, string? Key);
internal sealed record ReviewFindingInput(string? Status, string? Note, Guid? Reviewer);
internal sealed record ReferenceRequest(Guid ConnectionId, string Type, string ExternalId, string Purpose);
internal sealed record MatchRequest(Guid DocumentId, Guid ObservationId, string? Reason);
internal sealed record AccountingScenarioInput(string Scenario);

internal static partial class ClientConnectEndpoints
{
    private static void MapReconciliation(RouteGroupBuilder connect)
    {
        var group = connect.MapGroup("/reconciliation").WithTags("Reconciliation");
        group.MapGet("/coverage", ReadCoverage).AddEndpointFilter(Access(Coverage.View));
        group.MapPost("/coverage/preview", CoveragePreview).AddEndpointFilter(Access(Coverage.Manage));
        group.MapPost("/coverage", SaveCoverage).AddEndpointFilter(Access(Coverage.Manage, true));
        group.MapPost("/coverage/{id:guid}/activate", ActivateCoverage).AddEndpointFilter(Access(Coverage.Approve, true));
        group.MapPost("/jobs", QueueReconciliation).AddEndpointFilter(Access(Coverage.Run, true)).RequireRateLimiting("connector-operation");
        group.MapGet("/jobs", ReconciliationJobs).AddEndpointFilter(Access(Coverage.View));
        group.MapPost("/jobs/{id:guid}/cancel", CancelReconciliation).AddEndpointFilter(Access(Coverage.Run, true));
        group.MapGet("/overview", ReconciliationOverview).AddEndpointFilter(Access(Coverage.View));
        group.MapGet("/register", ReconciliationRegister).AddEndpointFilter(Access(Coverage.View));
        group.MapGet("/comparisons/{id:guid}", ReconciliationDetail).AddEndpointFilter(Access(Coverage.View));
        group.MapGet("/documents/{id:guid}", DocumentReconciliation).AddEndpointFilter(Access(Coverage.View));
        group.MapGet("/discrepancies", ReconciliationFindings).AddEndpointFilter(Access(Coverage.View));
        group.MapPost("/discrepancies/{id:guid}/review", ReviewDiscrepancy).AddEndpointFilter(Access(Coverage.Review, true));
        group.MapGet("/reports/{format}", ReconciliationReport).AddEndpointFilter(Access(Coverage.Export));
        group.MapGet("/references", ListAccountingReferences).AddEndpointFilter(Access(Coverage.References)).RequireRateLimiting("connector-operation");
        group.MapPost("/references", SaveAccountingReference).AddEndpointFilter(Access(Coverage.References, true)).RequireRateLimiting("connector-operation");
        group.MapPost("/matches", SaveAccountingMatch).AddEndpointFilter(Access(Coverage.Match, true));
        group.MapPost("/development/{id:guid}/external-revision", TestAccountingRevision).AddEndpointFilter(Access(Coverage.Manage, true));
    }

    private static IQueryable<ExternalConnection> AccountingLinks(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.ExternalConnections.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ResourceType == "CompanyFile" &&
            x.Version.Adapter == "mock-accounting").AsNoTracking();

    private static async Task<IResult> ReadCoverage(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var links = await AccountingLinks(db, subscriber, company).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.Status, x.ExternalResourceName, x.ExternalAccountId, x.ExternalResourceId, x.BranchId }).ToArrayAsync(ct);
        var rows = await Coverage.For(db, subscriber, company).AsNoTracking()
            .OrderByDescending(x => x.Number).Take(150).ToArrayAsync(ct);
        var active = await Coverage.Active(Coverage.For(db, subscriber, company), DateTimeOffset.UtcNow).Select(x => x.Id).ToArrayAsync(ct);
        return Results.Ok(new { links, items = rows.Select(x => CoverageView(x, active.Contains(x.Id))), canManage = Grant(http).Grants.Contains(Coverage.Manage) });
    }

    private static object CoverageView(CoverageRevision x, bool active) => new { x.Id, x.ConnectionId, x.Number, x.Adoption, x.Start, x.End, x.Mode,
        x.Types, x.Reason, x.ExclusionReason, x.ExcludedSources, x.IssuanceScope, x.TimeZone, x.AmountTolerance, x.FreshnessHours,
        x.IntervalHours, x.AfterSynchronization, x.BatchSize, x.CreatedBy, x.CreatedAt, x.ApprovedBy, x.EffectiveAt, active };

    private static async Task<IResult> CoveragePreview(Guid subscriber, Guid company, CoverageInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http) || await AccountingLinks(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == input.ConnectionId, ct) is not { } link)
            return TenantAccess.Missing();
        var proposed = Coverage.Validate(input, link, http.UserId());
        var observations = await db.AccountingObservations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.ConnectionId == link.Id && x.BusinessDate != null).Select(x => new { x.ExternalId, x.Type, x.BusinessDate, x.Source }).Distinct().Take(501).ToArrayAsync(ct);
        var checks = db.ReconciliationChecks.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == link.Id);
        var pinned = await db.SyncOperations.CountAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == link.Id, ct);
        var findings = await checks.CountAsync(x => x.FindingCount > 0, ct);
        return Results.Ok(new
        {
            valid = !await Coverage.Conflicts(db, link, proposed, DateTimeOffset.UtcNow, ct),
            proposed = CoverageView(proposed, false), pinnedCommitted = pinned, priorChecksWithFindings = findings,
            knownHistory = observations is { Length: > 0 and <= 500 } ? observations.Count(x => x.BusinessDate < proposed.Start) : (int?)null,
            knownOutside = observations is { Length: > 0 and <= 500 } && proposed.End is DateOnly end ?
                observations.Count(x => x.BusinessDate > end) : (int?)null,
            historyTotalKnown = false
        });
    }

    private static async Task<IResult> SaveCoverage(Guid subscriber, Guid company, CoverageInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http) || await AccountingLinks(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == input.ConnectionId, ct) is not { } link)
            return TenantAccess.Missing();
        if (input.AmountTolerance != 0 && !Grant(http).Grants.Contains(Coverage.Policy) ||
            (input.AfterSynchronization || input.IntervalHours != null) && !Grant(http).Grants.Contains(Coverage.Schedule)) return TenantAccess.Missing();
        var revision = Coverage.Validate(input, link, http.UserId());
        var latest = await Coverage.For(db, subscriber, company).Where(x => x.ConnectionId == link.Id).Select(x => (int?)x.Number).MaxAsync(ct) ?? 0;
        if (input.ExpectedVersion != latest) return Conflict("Coverage changed. Refresh before saving a new version.");
        revision.Number = latest + 1;
        db.CoverageRevisions.Add(revision);
        db.Audit("Reconciliation.CoverageDrafted", http.UserId(), subscriber, revision.Id);
        return Results.Created($"coverage/{revision.Id}", CoverageView(revision, false));
    }

    private static async Task<IResult> ActivateCoverage(Guid subscriber, Guid company, Guid id, ActivateCoverageInput input,
        HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http) || await Coverage.For(db, subscriber, company).Include(x => x.Connection).SingleOrDefaultAsync(x => x.Id == id, ct) is not { } revision)
            return TenantAccess.Missing();
        if (!input.Confirm) return ApiSupport.Invalid("confirm", "Explicitly confirm coverage activation.");
        if (revision.EffectiveAt.HasValue || await Coverage.For(db, subscriber, company).AnyAsync(x => x.ConnectionId == revision.ConnectionId &&
            x.Number > revision.Number && x.EffectiveAt != null, ct)) return Conflict("This version cannot be activated.");
        var now = DateTimeOffset.UtcNow;
        var effective = input.EffectiveAt ?? now;
        if (effective < now.AddMinutes(-1) || effective > now.AddYears(1)) return ApiSupport.Invalid("effectiveAt", "Use a current or future effective timestamp.");
        if (await Coverage.Conflicts(db, revision.Connection, revision, effective, ct) ||
            await Coverage.For(db, subscriber, company).AnyAsync(x => x.ConnectionId != revision.ConnectionId &&
                x.EffectiveAt > effective && x.Types.Any(t => revision.Types.Contains(t)) &&
                (x.Connection.BranchId == null || revision.Connection.BranchId == null || x.Connection.BranchId == revision.Connection.BranchId) &&
                x.Start <= (revision.End ?? DateOnly.MaxValue) && revision.Start <= (x.End ?? DateOnly.MaxValue), ct))
            return Conflict("The coverage overlaps another approved accounting connection for a shared document type and branch.");
        revision.ApprovedBy = http.UserId();
        revision.EffectiveAt = effective;
        db.Audit("Reconciliation.CoverageActivated", http.UserId(), subscriber, revision.Id);
        return Results.Ok(CoverageView(revision, effective <= now));
    }

    private static async Task<IResult> QueueReconciliation(Guid subscriber, Guid company, ReconciliationRequest input, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, CancellationToken ct)
    {
        if (!MockAccess(http) || await runtime.Plan(subscriber, ct) is not { } plan || !plan.ConnectorCategories.Contains("Accounting"))
            return TenantAccess.Missing();
        var kind = input.Kind ?? "Document";
        if (kind is not ("Document" or "Batch" or "Discovery") || kind == "Document" != input.DocumentId.HasValue ||
            kind != "Document" && !Grant(http).Grants.Contains(Coverage.Batch)) return TenantAccess.Missing();
        if (input.From > input.Through || input.From is not null && input.Through is not null &&
            input.Through.Value.DayNumber - input.From.Value.DayNumber > 400)
            return ApiSupport.Invalid("period", "Use a bounded range of up to 400 days.");
        var linkId = input.ConnectionId;
        if (input.DocumentId is Guid document)
        {
            var scope = db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == document);
            if (!await scope.AnyAsync(ct)) return TenantAccess.Missing();
            var pin = await db.SyncOperations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                x.DocumentId == document).Select(x => (Guid?)x.ConnectionId).SingleOrDefaultAsync(ct);
            var reliable = pin ?? await db.AccountingMatches.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == document)
                .Select(x => (Guid?)x.ConnectionId).SingleOrDefaultAsync(ct);
            if (reliable is not null && linkId is not null && reliable != linkId) return Conflict("This committed document has a different pinned accounting destination.");
            linkId = reliable ?? linkId;
            if (linkId is Guid target)
            {
                var branch = await AccountingLinks(db, subscriber, company).Where(x => x.Id == target).Select(x => x.BranchId).SingleOrDefaultAsync(ct);
                if (branch is not null && !await scope.AnyAsync(x => x.BranchId == branch, ct)) return TenantAccess.Missing();
            }
        }
        if (linkId is not Guid connection || !await AccountingLinks(db, subscriber, company).AnyAsync(x => x.Id == connection, ct))
            return Conflict("Choose an authorized accounting connection.");
        var active = await Coverage.Active(Coverage.For(db, subscriber, company), DateTimeOffset.UtcNow).AsNoTracking()
            .SingleOrDefaultAsync(x => x.ConnectionId == connection, ct);
        if (kind == "Discovery" && active is null) return Conflict("Activate a coverage version before external discovery.");
        if (kind == "Discovery" && (input.From is null || input.Through is null))
            return ApiSupport.Invalid("period", "Choose both dates for a manual discovery.");
        if (!Guid.TryParse(input.Key, out var requestKey))
            return ApiSupport.Invalid("key", "Provide a reconciliation operation identifier.");
        var key = "manual:" + requestKey.ToString("N");
        var old = await db.ReconciliationJobs.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.Key == key, ct);
        if (old is not null) return old.DocumentId == input.DocumentId && old.ConnectionId == connection && old.Kind == kind &&
            old.From == input.From && old.Through == input.Through ? Results.Accepted(value: JobView(old)) : Conflict("The operation identifier is already in use.");
        var open = await db.ReconciliationJobs.AsNoTracking().FirstOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.ConnectionId == connection && x.DocumentId == input.DocumentId && x.Kind == kind && x.From == input.From && x.Through == input.Through &&
            (x.State == "Queued" || x.State == "Processing" || x.State == "RetryScheduled"), ct);
        if (open is not null) return Results.Accepted(value: JobView(open));
        var job = new ReconciliationJob { SubscriberId = subscriber, CompanyId = company, ConnectionId = connection,
            CoverageId = active?.Id, DocumentId = input.DocumentId, Kind = kind, From = input.From, Through = input.Through, Key = key };
        db.ReconciliationJobs.Add(job);
        db.Audit("Reconciliation.JobQueued", http.UserId(), subscriber, job.Id);
        return Results.Accepted(value: JobView(job));
    }

    private static object JobView(ReconciliationJob x) => new { x.Id, x.DocumentId, x.ConnectionId, x.CoverageId, x.Kind,
        x.State, x.Failure, x.From, x.Through, x.Attempts, x.CreatedAt, x.CompletedAt, x.AvailableAt };

    private static async Task<IResult> ReconciliationJobs(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var items = await db.ReconciliationJobs.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .OrderByDescending(x => x.CreatedAt).Take(100).ToArrayAsync(ct);
        return Results.Ok(new { items = items.Select(JobView) });
    }

    private static async Task<IResult> CancelReconciliation(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var job = await db.ReconciliationJobs.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id, ct);
        if (job is null) return TenantAccess.Missing();
        if (job.Kind != "Document" && !Grant(http).Grants.Contains(Coverage.Batch)) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        var changed = await db.ReconciliationJobs.Where(x => x.Id == id && x.SubscriberId == subscriber && x.CompanyId == company &&
            x.State == "Queued" && x.Attempts == 0 && x.DocumentCursor == null)
            .ExecuteUpdateAsync(x => x.SetProperty(t => t.State, "Cancelled").SetProperty(t => t.CompletedAt, now), ct);
        if (changed == 0) return Conflict("Only unstarted reconciliation jobs may be cancelled.");
        db.Audit("Reconciliation.JobCancelled", http.UserId(), subscriber, id);
        return Results.NoContent();
    }

    private static async Task<IResult> TestAccountingRevision(Guid subscriber, Guid company, Guid id, AccountingScenarioInput input, HttpContext http,
        EfiscalDbContext db, DevelopmentAccountingRead accounting, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        string[] scenarios = ["Exact", "Vat", "Discount", "Customer", "Quantity", "Price", "TaxClass", "MissingLine", "ExtraLine", "DuplicateLine",
            "Reorder", "Unsupported", "Missing", "Duplicate", "Unavailable", "RateLimit", "IncompletePagination"];
        if (!scenarios.Contains(input.Scenario)) return ApiSupport.Invalid("scenario", "Choose an approved development scenario.");
        var pin = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == id, ct);
        if (pin is null || await AccountingLinks(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == pin.ConnectionId, ct) is not { } link ||
            await accounting.Authorize(link, ct) is null || !await db.MockAccountingRecords.AnyAsync(x => x.OperationId == pin.Id &&
                x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == pin.ConnectionId, ct)) return TenantAccess.Missing();
        var number = await db.MockAccountingRevisions.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.OperationId == pin.Id)
            .Select(x => (int?)x.Number).MaxAsync(ct) ?? 0;
        db.MockAccountingRevisions.Add(new() { SubscriberId = subscriber, CompanyId = company, ConnectionId = pin.ConnectionId,
            OperationId = pin.Id, Number = number + 1, Scenario = input.Scenario });
        db.Audit("Reconciliation.DevelopmentExternalRevision", http.UserId(), subscriber, id);
        return Results.Ok(new { version = number + 1, input.Scenario });
    }
}

