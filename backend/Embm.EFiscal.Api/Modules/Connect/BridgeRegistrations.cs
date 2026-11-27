using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record BridgeRegistrationInput(string? PublicKeyFingerprint, string? AgentVersion, BridgeAdapterInput[]? InstalledAdapters);
internal sealed record BridgeAdapterInput(string? Code, string? Version);

internal static partial class ClientConnectEndpoints
{
    private static void MapBridge(RouteGroupBuilder group)
    {
        group.MapGet("/bridge/registrations", ListBridges).AddEndpointFilter(Access(Manage));
        group.MapPost("/bridge/registrations", RegisterBridge).AddEndpointFilter(Access(Manage, true));
        group.MapPost("/bridge/registrations/{id:guid}/revoke", RevokeBridge).AddEndpointFilter(Access(Manage, true));
    }

    private static IQueryable<DesktopAgentRegistration> BridgeScope(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.DesktopAgentRegistrations.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> ListBridges(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var registrations = await BridgeScope(db, subscriber, company).AsNoTracking()
            .OrderByDescending(x => x.RegisteredAt).Take(100).ToArrayAsync(ct);
        return Results.Ok(new { items = registrations.Select(BridgeView), runtimeAvailable = false });
    }

    private static async Task<IResult> RegisterBridge(Guid subscriber, Guid company, BridgeRegistrationInput input, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var fingerprint = SpecImport.Text(input.PublicKeyFingerprint, 64).ToUpperInvariant();
        var version = SpecImport.Text(input.AgentVersion, 45);
        Require(fingerprint.Length == 64 && fingerprint.All(char.IsAsciiHexDigit) && version != "",
            "Supply a SHA-256 public-key fingerprint and an agent version.");
        Require((input.InstalledAdapters?.Length ?? 0) <= 20, "At most 20 adapter declarations are supported.");
        var adapters = (input.InstalledAdapters ?? []).Select(adapter =>
        {
            if (adapter is null) throw new ConnectorInputError("Supply an adapter code and version.");
            var code = Code(adapter.Code);
            var adapterVersion = SpecImport.Text(adapter.Version, 45);
            Require(adapterVersion != "", "Supply an adapter version.");
            return new { code, version = adapterVersion };
        }).ToArray();
        Require(adapters.Select(x => x.code).Distinct().Count() == adapters.Length, "Adapter codes must be unique.");
        var scope = BridgeScope(db, subscriber, company);
        if (await scope.AnyAsync(x => x.PublicKeyFingerprint == fingerprint, ct))
            return Conflict("This key fingerprint already has a registration for the company.");
        if (await scope.CountAsync(x => !x.RevokedAt.HasValue, ct) >= 10)
            return Conflict("This company has ten pending registrations.");
        var row = new DesktopAgentRegistration { SubscriberId = subscriber, CompanyId = company,
            PublicKeyFingerprint = fingerprint, AgentVersion = version, InstalledAdapters = Freeze(adapters) };
        db.DesktopAgentRegistrations.Add(row);
        db.Audit("Connections.BridgeRegistrationCreated", http.UserId(), subscriber, row.Id);
        return Results.Created("/api/workspace/" + subscriber + "/companies/" + company + "/connect/bridge/registrations", BridgeView(row));
    }

    private static async Task<IResult> RevokeBridge(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var row = await BridgeScope(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        if (row.RevokedAt is null)
        {
            row.RevokedAt = DateTimeOffset.UtcNow;
            db.Audit("Connections.BridgeRegistrationRevoked", http.UserId(), subscriber, row.Id);
        }
        return Results.Ok(BridgeView(row));
    }

    private static object BridgeView(DesktopAgentRegistration row) => new
    {
        row.Id, row.PublicKeyFingerprint, row.AgentVersion, row.InstalledAdapters, row.RegisteredAt, row.LastHeartbeatAt,
        row.RevokedAt, status = row.RevokedAt is null ? "Pending Agent Approval" : "Revoked"
    };
}

