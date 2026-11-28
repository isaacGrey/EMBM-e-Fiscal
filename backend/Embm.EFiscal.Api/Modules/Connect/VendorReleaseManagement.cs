using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal static partial class VendorConnectEndpoints
{
    private static void MapVendorReleaseManagement(RouteGroupBuilder group)
    {
        group.MapPost("/{id:guid}/resume", Resume).AddEndpointFilter(Grant(Publish));
        group.MapPost("/{id:guid}/versions/{version:guid}/revoke", RevokeVersion).AddEndpointFilter(Grant(Publish));
        group.MapGet("/{id:guid}/versions/{version:guid}/impact", ReleaseImpact).AddEndpointFilter(Grant(View));
    }

    private static async Task<IResult> Resume(Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        if (connector is null) return Missing();
        if (!connector.Disabled || connector.Lifecycle == "Retired" || !await db.ConnectorVersions.AnyAsync(x =>
            x.ConnectorId == id && x.State == "Published" && x.Revocation == null && (x.Adapter == "mock-accounting" || x.Adapter == "mock-commerce"), ct))
            return Conflict("Resume requires a disabled connector with a usable published development version.");
        connector.Disabled = false;
        await Complete(db, http, "Resumed", id, tx, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeVersion(Guid id, Guid version, RevokeConnectorVersion input, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        Require(ApiSupport.Text(input.Reason, 500), "Record a reason for this release revocation.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var release = await db.ConnectorVersions.SingleOrDefaultAsync(x => x.Id == version && x.ConnectorId == id, ct);
        if (connector is null || release is null) return Missing();
        if (release.State != "Published" || release.Revocation is not null || !IsMock(release.Adapter))
            return Conflict("Only a published development version that has not already been revoked can be revoked.");
        db.Add(new ConnectorVersionRevocation { VersionId = release.Id, ActorId = http.UserId(), Reason = input.Reason.Trim() });
        db.Audit("Connectors.VersionRevoked", http.UserId(), target: release.Id);
        await Complete(db, http, "VersionRevoked", id, tx, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ReleaseImpact(Guid id, Guid version, EfiscalDbContext db, CancellationToken ct)
    {
        var release = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector)
            .SingleOrDefaultAsync(x => x.Id == version && x.ConnectorId == id, ct);
        if (release is null) return Missing();

        var blocked = release.Connector.Disabled || release.Revocation is not null;
        var connections = await db.ExternalConnections.AsNoTracking().Where(x => x.ConnectorVersionId == version)
            .GroupBy(x => blocked && x.Status != "Disconnected" ? "Suspended" : x.Status)
            .Select(x => new { status = x.Key, count = x.Count() }).OrderBy(x => x.status).ToArrayAsync(ct);
        var operations = db.SyncOperations.AsNoTracking().Where(x => x.ConnectorVersionId == version);
        var sync = await operations.GroupBy(_ => 1).Select(x => new
        {
            unsubmitted = x.Count(o => o.SubmittedAt == null && o.ConfirmedAt == null),
            awaitingConfirmation = x.Count(o => o.SubmittedAt != null && o.ConfirmedAt == null),
            confirmed = x.Count(o => o.ConfirmedAt != null)
        }).SingleOrDefaultAsync(ct);
        var audit = await db.SecurityAudits.AsNoTracking().Where(x => x.TargetId == version && x.Action.StartsWith("Connectors."))
            .OrderByDescending(x => x.Id).Take(50).Select(x => new { x.Action, x.At, x.ActorId }).ToArrayAsync(ct);
        return Results.Ok(new { connections, sync = sync ?? new { unsubmitted = 0, awaitingConfirmation = 0, confirmed = 0 }, audit });
    }
}
