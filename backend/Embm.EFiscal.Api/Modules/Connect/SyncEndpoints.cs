using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record SyncDestinationInput(Guid ProfileId, Guid? BranchId, string DocumentType, bool Enabled, bool Automatic, string? Scenario);
internal sealed record SyncAction(string? Mode, int? ReplacementVersion);

internal static partial class ClientConnectEndpoints
{
    private static void MapAccounting(RouteGroupBuilder group)
    {
        group.MapGet("/accounting/destinations", SyncDestinations).AddEndpointFilter(Access(Manage));
        group.MapPut("/accounting/destinations", SetSyncDestination).AddEndpointFilter(Access(Manage, true));
        group.MapGet("/accounting/jobs", SyncRegister).AddEndpointFilter(Access(Manage));
        group.MapGet("/accounting/jobs/{document:guid}", SyncDetail).AddEndpointFilter(Access(Permissions.CompanyView));
        group.MapPost("/accounting/jobs/{document:guid}/retry", RetrySync).AddEndpointFilter(Access(Manage, true));
    }

    private static async Task<IResult> SyncDestinations(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var routes = await db.SyncDestinations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .OrderBy(x => x.DocumentType).ThenBy(x => x.BranchId).Take(100)
            .Select(x => new { x.Id, x.ProfileId, profile = x.Profile.Name, x.BranchId, x.DocumentType, x.Enabled, x.Automatic, x.Scenario,
                connection = x.Profile.Connection.Name, x.Profile.Connection.ExternalResourceName, health = x.Profile.Connection.Status }).ToArrayAsync(ct);
        return Results.Ok(new { routes, scenarios = AccountingRouting.Scenarios, documentTypes = DocumentRules.Types });
    }

    private static async Task<IResult> SetSyncDestination(Guid subscriber, Guid company, SyncDestinationInput input, HttpContext http, EfiscalDbContext db,
        ConnectionRuntime runtime, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, input.ProfileId, ct) is not { } profile ||
            profile.Direction != FieldMapper.Outbound || profile.DocumentType != input.DocumentType || profile.Connection.ResourceType != "CompanyFile")
            return TenantAccess.Missing();
        var scenario = input.Scenario ?? "Normal";
        Require(AccountingRouting.Scenarios.Contains(scenario), "Select an approved synthetic test outcome.");
        if (input.BranchId is Guid branch && !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == branch, ct))
            return TenantAccess.Missing();
        Require(profile.Connection.BranchId is null || profile.Connection.BranchId == input.BranchId, "The destination must use its bound company branch.");
        if (input.Enabled && (await runtime.Active(subscriber, company, profile.ConnectionId, [profile.Operation], false, ct) is null ||
            !await db.ExecutionPolicies.AnyAsync(x => x.ConnectorVersionId == profile.Connection.ConnectorVersionId && x.Capability == profile.Operation &&
                x.Method == "POST" && x.Path == "/v1/accounting/documents", ct))) return Conflict("An active accounting connection and approved write capability are required.");
        var route = await db.SyncDestinations.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.DocumentType == input.DocumentType && x.BranchId == input.BranchId, ct);
        if (route is null)
        {
            route = new SyncDestination { SubscriberId = subscriber, CompanyId = company, BranchId = input.BranchId, DocumentType = input.DocumentType };
            db.SyncDestinations.Add(route);
        }
        route.ProfileId = profile.Id;
        route.Enabled = input.Enabled;
        route.Automatic = input.Automatic;
        route.Scenario = scenario;
        db.Audit("Accounting.DestinationConfigured", http.UserId(), subscriber, route.Id);
        return Results.Ok(new { route.Id, route.ProfileId, route.BranchId, route.DocumentType, route.Enabled, route.Automatic, route.Scenario });
    }

    private static async Task<IResult> SyncRegister(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db,
        string? q, string? status, string? type, Guid? branch, Guid? connection, DateOnly? from, DateOnly? through, int? page, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var search = SpecImport.Text(q, 90).ToUpperInvariant();
        var visible = DocumentRules.Types.Where(t => Grant(http).Grants.Contains(DocumentRules.Permission(t, false))).ToArray();
        var docs = db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && visible.Contains(x.Type));
        if (search != "") docs = docs.Where(x => x.Number.ToUpper().Contains(search));
        if (status is not null) docs = docs.Where(x => x.Processing.Accounting == status);
        if (type is not null) docs = docs.Where(x => x.Type == type);
        if (branch is not null) docs = docs.Where(x => x.BranchId == branch);
        if (from is not null) docs = docs.Where(x => x.Date >= from);
        if (through is not null) docs = docs.Where(x => x.Date <= through);
        if (connection is not null) docs = docs.Where(x => db.SyncOperations.Any(op => op.SubscriberId == subscriber &&
            op.CompanyId == company && op.DocumentId == x.Id && op.ConnectionId == connection));
        var size = 25;
        var index = Math.Clamp(page ?? 1, 1, 200);
        var rows = await docs.OrderByDescending(x => x.Date).ThenByDescending(x => x.CommittedAt).Skip((index - 1) * size).Take(size)
            .Select(x => new { x.Id, x.Number, x.Type, x.Date, x.BranchId, status = x.Processing.Accounting }).ToArrayAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var pins = await db.SyncOperations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && ids.Contains(x.DocumentId))
            .ToDictionaryAsync(x => x.DocumentId, ct);
        var identities = await db.ExternalDocumentIdentities.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && ids.Contains(x.DocumentId))
            .ToDictionaryAsync(x => x.DocumentId, ct);
        var latest = await db.ExternalDocumentIdentities.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .MaxAsync(x => (DateTimeOffset?)x.ConfirmedAt, ct);
        return Results.Ok(new { page = index, pageSize = size, total = await docs.CountAsync(ct), latest,
            items = rows.Select(x => new { x.Id, x.Number, x.Type, x.Date, x.BranchId, x.status,
                connectionId = pins.GetValueOrDefault(x.Id)?.ConnectionId, externalNumber = identities.GetValueOrDefault(x.Id)?.ExternalNumber }) });
    }

    private static async Task<FiscalDocument?> VisibleDocument(Guid subscriber, Guid company, Guid document, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var row = await db.FiscalDocuments.AsNoTracking().Include(x => x.Processing)
            .SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == document, ct);
        return row is not null && Grant(http).Grants.Contains(DocumentRules.Permission(row.Type, false)) ? row : null;
    }

    private static async Task<IResult> SyncDetail(Guid subscriber, Guid company, Guid document, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http) || await VisibleDocument(subscriber, company, document, http, db, ct) is not { } doc)
            return TenantAccess.Missing();
        var sync = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == document, ct);
        var attempts = sync is null ? [] : await db.SyncAttempts.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.OperationId == sync.Id)
            .OrderByDescending(x => x.Number).Take(60).ToArrayAsync(ct);
        var external = await db.ExternalDocumentIdentities.AsNoTracking().SingleOrDefaultAsync(x =>
            x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == document, ct);
        return Results.Ok(new { doc.Id, doc.Type, doc.Number, status = doc.Processing.Accounting, identity = external, attempts,
            profileId = sync?.ProfileId, mappingVersion = sync?.MappingNumber, canManage = Grant(http).Grants.Contains(Manage),
            canVerify = sync?.SubmittedAt is not null && external is null, canRetry = sync?.SubmittedAt is null && external is null });
    }

    private static async Task<IResult> RetrySync(Guid subscriber, Guid company, Guid document, SyncAction input, HttpContext http,
        EfiscalDbContext db, AccountingRouting routing, CancellationToken ct)
    {
        if (!MockAccess(http) || await VisibleDocument(subscriber, company, document, http, db, ct) is not { DevelopmentFixture: true } doc)
            return TenantAccess.Missing();
        Require(input.Mode is "retry" or "verify", "Choose Retry or Verify external outcome.");
        var job = await db.DocumentOutboxMessages.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company
            && x.DocumentId == document && x.Kind == "Accounting" && x.Operation == "initial", ct);
        if (job is null) return TenantAccess.Missing();
        // Serialize the management action with worker claims and completion.
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "DocumentOutboxMessages" WHERE "Id" = {job.Id} FOR UPDATE""", ct);
        await db.Entry(job).ReloadAsync(ct);
        if (job.State is "Pending" or "Processing" or "VerifyPending" or "Synchronized") return Conflict("This job is already scheduled, active or confirmed.");
        var state = await db.DocumentProcessing.SingleAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == document, ct);
        var sync = await db.SyncOperations.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == document, ct);
        if (input.Mode == "verify")
        {
            if (sync?.SubmittedAt is null || input.ReplacementVersion.HasValue) return Conflict("Verification requires a prior submission and its original mapping.");
            job.State = "VerifyPending";
        }
        else if (sync is null)
        {
            if (input.ReplacementVersion.HasValue) return Conflict("Configure an active destination before retrying this document.");
            await routing.Pin(doc, job, state, false, ct);
            if (job.State != "Pending") return Conflict("Configure an active destination and mapping before retrying this document.");
        }
        else
        {
            if (sync.SubmittedAt.HasValue) return Conflict("Verify an uncertain or submitted record before considering another write.");
            if (input.ReplacementVersion is int number)
            {
                if (!await db.MappingVersions.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ProfileId == sync.ProfileId
                    && x.Number == number && x.ConnectorVersionId == sync.ConnectorVersionId && x.Validated && x.ActivatedAt != null, ct))
                    return Conflict("Select an authorized activated replacement mapping for the same destination.");
                sync.MappingNumber = number;
                db.Audit("Accounting.ReplacementMappingSelected", http.UserId(), subscriber, document);
            }
            job.State = "Pending";
        }
        job.AvailableAt = DateTimeOffset.UtcNow;
        state.Accounting = "Queued";
        db.Audit("Accounting." + (input.Mode == "verify" ? "VerificationRequested" : "RetryRequested"), http.UserId(), subscriber, document);
        return Results.Accepted(value: new { job.Id, state.Accounting });
    }
}

