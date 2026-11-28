using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class ProviderReadinessEvent
{
    public Guid VersionId { get; init; }
    public int Sequence { get; init; }
    public int ConnectorRevision { get; init; }
    public required string Action { get; init; }
    public JsonElement? Evidence { get; init; }
    public string? Reason { get; init; }
    public Guid ActorId { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed record ProviderEvidence(string? Edition, string? Market, string? DocumentationUrl,
    string? PartnerAccessReference, string? AuthorizationReference, DateOnly? AuthorizationThrough,
    string? SandboxReportReference, string[]? Capabilities);
internal sealed record RecordProviderEvidence(int ExpectedSequence, int ExpectedRevision, ProviderEvidence Evidence);
internal sealed record ReviewProviderEvidence(int ExpectedSequence, int ExpectedRevision, string Decision, string Reason);

internal static partial class VendorConnectEndpoints
{
    private static readonly Dictionary<string, string> ProviderDocumentationHosts = new(StringComparer.Ordinal)
    {
        ["myob-business"] = "myob.com",
        ["myob-accountright-online"] = "myob.com",
        ["myob-accountright-desktop"] = "myob.com",
        ["qbd"] = "intuit.com",
        ["qbo"] = "intuit.com",
        ["shopee"] = "shopee.com",
        ["lazada"] = "lazada.com",
        ["tiktok"] = "tiktokshop.com"
    };

    private static void MapProviderReadiness(RouteGroupBuilder group)
    {
        group.MapGet("/production-readiness", ProviderCandidates).AddEndpointFilter(Grant(View));
        var scoped = group.MapGroup("/{id:guid}/versions/{version:guid}/production-readiness");
        scoped.MapGet("", ProviderReadiness).AddEndpointFilter(Grant(View));
        scoped.MapPost("/evidence", SaveProviderEvidence).AddEndpointFilter(Grant(Edit)).RequireRateLimiting("connector-operation");
        scoped.MapPost("/review", ReviewProvider).AddEndpointFilter(Grant(ApproveRelease)).RequireRateLimiting("connector-operation");
    }

    private static IQueryable<ConnectorVersion> ProviderVersions(EfiscalDbContext db) =>
        db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).Where(x => x.Adapter == "review-only");

    private static async Task<IResult> ProviderCandidates(EfiscalDbContext db, CancellationToken ct)
    {
        var candidates = await ProviderVersions(db).OrderBy(x => x.Connector.Code).ThenByDescending(x => x.Number).Take(250).ToArrayAsync(ct);
        return Results.Ok(new
        {
            productionAccessEnabled = false,
            items = candidates.Select(x => new { x.Id, x.ConnectorId, x.Connector.Code, x.Number, x.Revision, name = Read(x).Name })
        });
    }

    private static IQueryable<ProviderReadinessEvent> ProviderEvents(EfiscalDbContext db, Guid version) =>
        db.Set<ProviderReadinessEvent>().AsNoTracking().Where(x => x.VersionId == version).OrderByDescending(x => x.Sequence);

    private static async Task<IResult> ProviderReadiness(Guid id, Guid version, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var release = await ProviderVersions(db).SingleOrDefaultAsync(x => x.ConnectorId == id && x.Id == version, ct);
        if (release is null) return Missing();
        var history = await ProviderEvents(db, version).Take(50).ToArrayAsync(ct);
        var head = history.FirstOrDefault();
        var priorEvidence = history.FirstOrDefault(x => x.Action == "EvidenceRecorded");
        var evidence = priorEvidence?.Evidence?.Deserialize<ProviderEvidence>(SpecImport.Json);
        var missing = MissingProviderEvidence(evidence, RegulatoryEligibility.ManilaDate(DateTimeOffset.UtcNow));
        var permissions = await db.VendorConnectorGrants.AsNoTracking().Where(x => x.UserId == http.UserId())
            .Select(x => x.Permission).ToArrayAsync(ct);
        var configuration = Read(release);
        return Results.Ok(new
        {
            release.Id, release.ConnectorId, release.Connector.Code, release.Number, release.Revision,
            configuration.Name, configuration.Documentation,
            capabilities = configuration.Capabilities.Select(x => x.Code),
            sequence = head?.Sequence ?? 0,
            status = head is null ? "AwaitingEvidence" : head.Action == "Withdrawn" ? "Withdrawn" :
                head.ConnectorRevision != release.Revision ? "EvidenceStale" :
                head.Action == "EvidenceReviewed" && missing.Length > 0 ? "AuthorizationExpired" : head.Action,
            evidence, missing, history,
            canRecord = permissions.Contains(Edit),
            canReview = permissions.Contains(ApproveRelease) && head?.Action == "EvidenceRecorded" &&
                head.ConnectorRevision == release.Revision && head.ActorId != http.UserId(),
            canWithdraw = permissions.Contains(ApproveRelease) && head?.Action is "EvidenceRecorded" or "EvidenceReviewed",
            productionAccessEnabled = false
        });
    }

    private static async Task<IResult> SaveProviderEvidence(Guid id, Guid version, RecordProviderEvidence input, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var release = await ProviderVersions(db).SingleOrDefaultAsync(x => x.ConnectorId == id && x.Id == version, ct);
        if (connector is null || release is null) return Missing();
        var head = await ProviderEvents(db, version).FirstOrDefaultAsync(ct);
        if (input.ExpectedRevision != release.Revision || input.ExpectedSequence != (head?.Sequence ?? 0))
            return Conflict("Reload the connector and evidence history before recording another change.");
        var evidence = NormalizeProviderEvidence(input.Evidence, release);
        var next = new ProviderReadinessEvent { VersionId = version, Sequence = input.ExpectedSequence + 1,
            ConnectorRevision = release.Revision, Action = "EvidenceRecorded", Evidence = Freeze(evidence), ActorId = http.UserId() };
        db.Add(next);
        db.Audit("Connectors.ProviderEvidenceRecorded", next.ActorId, target: version);
        await Complete(db, http, "ProviderEvidenceRecorded", id, tx, ct);
        return Results.Ok(next);
    }

    private static async Task<IResult> ReviewProvider(Guid id, Guid version, ReviewProviderEvidence input, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        Require(input.Decision is "EvidenceReviewed" or "ChangesRequested" or "Withdrawn" &&
            ApiSupport.Text(input.Reason, 500), "Choose a review decision and provide a reason.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var release = await ProviderVersions(db).SingleOrDefaultAsync(x => x.ConnectorId == id && x.Id == version, ct);
        if (connector is null || release is null) return Missing();
        var head = await ProviderEvents(db, version).FirstOrDefaultAsync(ct);
        if (head is null || input.ExpectedSequence != head.Sequence || input.ExpectedRevision != release.Revision)
            return Conflict("Reload the connector and evidence history before recording another change.");
        if (input.Decision == "Withdrawn")
        {
            if (head.Action is not ("EvidenceRecorded" or "EvidenceReviewed"))
                return Conflict("Only submitted or reviewed evidence can be withdrawn.");
        }
        else
        {
            if (head.Action != "EvidenceRecorded" || head.ConnectorRevision != release.Revision || head.ActorId == http.UserId())
                return Conflict("An independent reviewer must review evidence for the current connector revision.");
            if (input.Decision == "EvidenceReviewed" && MissingProviderEvidence(head.Evidence?.Deserialize<ProviderEvidence>(SpecImport.Json),
                    RegulatoryEligibility.ManilaDate(DateTimeOffset.UtcNow)).Length > 0)
                return Conflict("Complete the provider evidence and record a current authorization before confirming review.");
        }
        var next = new ProviderReadinessEvent { VersionId = version, Sequence = head.Sequence + 1,
            ConnectorRevision = input.Decision == "Withdrawn" ? head.ConnectorRevision : release.Revision,
            Action = input.Decision, Reason = input.Reason.Trim(), ActorId = http.UserId() };
        db.Add(next);
        db.Audit("Connectors.Provider" + next.Action, next.ActorId, target: version);
        await Complete(db, http, "Provider" + next.Action, id, tx, ct);
        return Results.Ok(next);
    }

    private static ProviderEvidence NormalizeProviderEvidence(ProviderEvidence? value, ConnectorVersion release)
    {
        Require(value is not null, "Supply provider evidence.");
        var evidence = value!;
        Require(ApiSupport.Optional(evidence.Edition, 120), "Use a provider edition of up to 120 characters.");
        var market = evidence.Market?.Trim().ToUpperInvariant();
        Require(string.IsNullOrEmpty(market) || market is { Length: 2 } && market.All(char.IsAsciiLetterUpper),
            "Use a two-letter market code.");
        var capabilities = evidence.Capabilities ?? [];
        var declared = Read(release).Capabilities.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        Require(capabilities.Length <= 40 && capabilities.Distinct().Count() == capabilities.Length && capabilities.All(declared.Contains),
            "Use capability codes declared on this review-only connector version.");
        var documentation = string.IsNullOrWhiteSpace(evidence.DocumentationUrl) ? null : SpecImport.Https(evidence.DocumentationUrl, true);
        if (documentation is not null)
        {
            // A registered source is evidence metadata. The server never fetches it.
            var source = new Uri(documentation);
            Require(Read(release).Documentation.Contains(documentation) &&
                (!ProviderDocumentationHosts.TryGetValue(release.Connector.Code, out var host) || source.IdnHost == host ||
                 source.IdnHost.EndsWith("." + host, StringComparison.Ordinal)),
                "Choose a documentation URL registered for this provider version.");
        }
        return new(BlankToNull(evidence.Edition), BlankToNull(market), documentation,
            ProviderReference(evidence.PartnerAccessReference), ProviderReference(evidence.AuthorizationReference),
            evidence.AuthorizationThrough, ProviderReference(evidence.SandboxReportReference), capabilities);
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? ProviderReference(string? value)
    {
        var reference = BlankToNull(value);
        Require(reference is null || reference.Length <= 160 && reference.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/'),
            "Use a document reference ID of up to 160 characters.");
        return reference;
    }

    private static string[] MissingProviderEvidence(ProviderEvidence? evidence, DateOnly today)
    {
        var missing = new List<string>();
        if (evidence?.Edition is null) missing.Add("Provider edition");
        if (evidence?.Market is null) missing.Add("Market");
        if (evidence?.DocumentationUrl is null) missing.Add("Official documentation reference");
        if (evidence?.PartnerAccessReference is null) missing.Add("Partner or account access evidence");
        if (evidence?.AuthorizationReference is null) missing.Add("Provider authorization evidence");
        if (evidence?.AuthorizationThrough is null || evidence.AuthorizationThrough < today) missing.Add("Current authorization validity");
        if (evidence?.SandboxReportReference is null) missing.Add("Authorized sandbox test report");
        if (evidence?.Capabilities is not { Length: > 0 }) missing.Add("Reviewed capability scope");
        return missing.ToArray();
    }
}
