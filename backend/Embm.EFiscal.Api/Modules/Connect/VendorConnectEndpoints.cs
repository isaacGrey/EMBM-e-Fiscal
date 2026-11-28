using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ConnectorRevision(int ExpectedRevision);
internal sealed record ConnectorLifecycle(string State);
internal sealed record RevokeConnectorVersion(string Reason);

internal static partial class VendorConnectEndpoints
{
    public static void MapVendorConnect(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment()) return;
        var group = app.MapGroup("/api/vendor/connectors").RequireAuthorization("vendor")
            .AddEndpointFilter(ApiSupport.Protect).WithTags("Universal API Connector Builder");
        MapVendorMapping(group);
        MapVendorExecution(group);
        MapVendorReleaseManagement(group);
        MapProviderReadiness(group);
        group.MapGet("", Catalog).AddEndpointFilter(Grant(View));
        group.MapGet("/choices", () => Results.Ok(new { categories = Categories, deployments = Deployments, authentication = Authentication, accounting = Accounting, commerce = Commerce }))
            .AddEndpointFilter(Grant(View));
        group.MapGet("/{id:guid}", Detail).AddEndpointFilter(Grant(View));
        group.MapPost("", Create).AddEndpointFilter(Grant(Edit));
        group.MapPut("/{id:guid}/versions/{version:guid}", Update).AddEndpointFilter(Grant(Edit));
        group.MapPost("/{id:guid}/versions/{version:guid}/import", Import).AddEndpointFilter(Grant(Edit))
            .DisableAntiforgery().RequireRateLimiting("connector-operation").WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(SpecImport.MaxBytes + 16_384));
        group.MapPost("/{id:guid}/versions/{version:guid}/validate", Validate).AddEndpointFilter(Grant(Edit));
        group.MapPost("/{id:guid}/versions/{version:guid}/approve", Approve).AddEndpointFilter(Grant(ApproveRelease));
        group.MapPost("/{id:guid}/versions/{version:guid}/publish", PublishVersion).AddEndpointFilter(Grant(Publish));
        group.MapPost("/{id:guid}/versions/{version:guid}/copy", Copy).AddEndpointFilter(Grant(Edit));
        group.MapPost("/{id:guid}/disable", Disable).AddEndpointFilter(Grant(Publish));
        group.MapPost("/{id:guid}/lifecycle", Lifecycle).AddEndpointFilter(Grant(Publish));
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Grant(string permission) => async (context, next) =>
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        if (!await db.VendorConnectorGrants.AnyAsync(x => x.UserId == http.UserId() && x.Permission == permission, http.RequestAborted))
            return ApiSupport.Problem(403, "Connector Builder permission is required.");
        try { return await next(context); }
        catch (ConnectorInputError error) { return ApiSupport.Invalid("connector", error.Message); }
        catch (InvalidDataException) { return ApiSupport.Invalid("specification", "The upload is invalid or too large."); }
    };

    private static async Task<IResult> Catalog(EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        var rows = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector)
            .Where(x => !db.ConnectorVersions.Any(v => v.ConnectorId == x.ConnectorId && v.Number > x.Number))
            .OrderBy(x => x.Connector.Code).Take(250).ToArrayAsync(ct);
        var grants = await db.VendorConnectorGrants.Where(x => x.UserId == http.UserId()).Select(x => x.Permission).ToArrayAsync(ct);
        return Results.Ok(new { items = rows.Select(x => Present(x, false)), canEdit = grants.Contains(Edit),
            canApprove = grants.Contains(ApproveRelease), canPublish = grants.Contains(Publish) });
    }

    private static async Task<IResult> Detail(Guid id, EfiscalDbContext db, CancellationToken ct)
    {
        var versions = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).Where(x => x.ConnectorId == id).OrderByDescending(x => x.Number).Take(80).ToArrayAsync(ct);
        if (versions.Length == 0) return Missing();
        var audit = await db.SecurityAudits.AsNoTracking().Where(x => x.TargetId == id && x.Action.StartsWith("Connectors."))
            .OrderByDescending(x => x.Id).Take(80).Select(x => new { x.Action, x.At, x.ActorId }).ToArrayAsync(ct);
        return Results.Ok(new { versions = versions.Select(x => Present(x, true)), audit });
    }

    private static async Task<IResult> Create(ConnectorDraft input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var code = Code(input.Code);
        var config = Normalize(input);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({code}, 61343))", ct);
        if (await db.Connectors.AnyAsync(x => x.Code == code, ct)) return Conflict("This connector code already exists.");
        var connector = new Connector { Code = code };
        var version = new ConnectorVersion { Connector = connector, ConnectorId = connector.Id, Number = 1, ActorId = http.UserId(),
            Adapter = input.Adapter, Category = input.Category, Configuration = Freeze(config) };
        db.ConnectorVersions.Add(version);
        db.Audit("Connectors.Created", http.UserId(), target: version.Id);
        await Complete(db, http, "Created", connector.Id, tx, ct);
        return Results.Created("/api/vendor/connectors/" + connector.Id, Present(version, true));
    }

    private static Task<IResult> Update(Guid id, Guid version, ConnectorDraft input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
        Change(db, http, id, version, input.ExpectedRevision, "DraftUpdated", (v, _) =>
        {
            if (!Editable(v)) return Conflict("Copy the approved or published version to make changes.");
            v.Configuration = Freeze(Normalize(input));
            v.Category = input.Category;
            v.Adapter = input.Adapter;
            Invalidate(v);
            return null;
        }, ct);

    private static async Task<IResult> Import(Guid id, Guid version, IFormFile file, int expectedRevision, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        Require(extension is ".yaml" or ".yml" or ".json", "Use a JSON, YAML or YML specification file.");
        Require(file.Length is > 0 and <= SpecImport.MaxBytes, "Upload a specification between 1 byte and 512 KiB.");
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);
        var spec = SpecImport.Parse(memory.ToArray(), extension != ".json");
        return await Change(db, http, id, version, expectedRevision, "SpecificationImported", (v, _) =>
        {
            if (!Editable(v)) return Conflict("Copy the approved or published version before importing a specification.");
            v.Specification = Freeze(spec);
            Invalidate(v);
            return null;
        }, ct);
    }

    private static Task<IResult> Validate(Guid id, Guid version, ConnectorRevision input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
        Change(db, http, id, version, input.ExpectedRevision, "ValidationRecorded", (v, _) =>
        {
            if (!Editable(v)) return Conflict("This version is already approved or published.");
            var c = Read(v);
            var spec = v.Specification?.Deserialize<SpecReview>(SpecImport.Json);
            List<string> errors = [];
            if (!IsMock(v.Adapter) && c.ManualOperations.Length == 0 && spec is null) errors.Add("Add manual operations or import an API specification.");
            if (!IsMock(v.Adapter) && c.Origins.Length == 0 && spec?.Servers.Length is not > 0) errors.Add("Declare an HTTPS origin for review.");
            if (IsMock(v.Adapter) && !c.Authentication.Contains("Synthetic Authorization")) errors.Add("Select Synthetic Authorization for a mock adapter.");
            var required = v.Adapter == "mock-accounting" ? "DiscoverCompanyFiles" : "DiscoverShops";
            if (IsMock(v.Adapter) && !c.Capabilities.Any(x => x.Code == required && x.Verification == "Development Verified"))
                errors.Add("Declare the development-verified discovery capability for this adapter.");
            v.Validation = Freeze(new { valid = errors.Count == 0, errors, warnings = spec?.Warnings ?? [], validatedAt = DateTimeOffset.UtcNow });
            v.State = errors.Count == 0 ? "Validated" : "Awaiting Validation";
            return null;
        }, ct);

    private static Task<IResult> Approve(Guid id, Guid version, ConnectorRevision input, HttpContext http, EfiscalDbContext db, CancellationToken ct) =>
        Change(db, http, id, version, input.ExpectedRevision, "DevelopmentApproved", (v, _) =>
        {
            if (v.State != "Validated" || !IsMock(v.Adapter)) return Conflict("Validate a registered mock adapter before approving development publication.");
            v.State = "Approved";
            return null;
        }, ct);

    private static async Task<IResult> PublishVersion(Guid id, Guid version, ConnectorRevision input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var release = await db.ConnectorVersions.FindAsync([version], ct);
        if (connector is null || release?.ConnectorId != id) return Missing();
        if (release.Revision != input.ExpectedRevision) return Conflict("Reload this version before making another change.");
        if (release.State != "Approved" || !IsMock(release.Adapter) || connector.Lifecycle == "Retired")
            return Conflict("Development publication requires an approved mock and a connector that is not retired.");
        if (await db.ConnectorVersions.AnyAsync(x => x.ConnectorId == id && x.State == "Published" && x.Number > release.Number, ct))
            return Conflict("Create and approve a new version to publish after a newer release.");
        release.State = "Published";
        release.Availability = "Development Verified";
        release.PublishedAt = DateTimeOffset.UtcNow;
        release.Revision++;
        db.Audit("Connectors.DevelopmentPublished", http.UserId(), target: release.Id);
        await Complete(db, http, "DevelopmentPublished", id, tx, ct);
        return Results.Ok(Present(release, true));
    }

    private static async Task<IResult> Copy(Guid id, Guid version, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var existing = await db.ConnectorVersions.FindAsync([version], ct);
        if (connector is null || existing?.ConnectorId != id) return Missing();
        var number = await db.ConnectorVersions.Where(x => x.ConnectorId == id).MaxAsync(x => x.Number, ct);
        var copy = new ConnectorVersion { Connector = connector, ConnectorId = id, Number = number + 1, Adapter = existing.Adapter,
            Category = existing.Category, Configuration = existing.Configuration, Specification = existing.Specification, ActorId = http.UserId() };
        db.Add(copy);
        db.Audit("Connectors.VersionCreated", http.UserId(), target: copy.Id);
        await Complete(db, http, "VersionCreated", id, tx, ct);
        return Results.Ok(Present(copy, true));
    }

    private static async Task<IResult> Disable(Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        if (connector is null) return Missing();
        connector.Disabled = true;
        await Complete(db, http, "Disabled", id, tx, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Lifecycle(Guid id, ConnectorLifecycle input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        Require(input.State is "Deprecated" or "Retired", "Choose Deprecated or Retired.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        if (connector is null || !await db.ConnectorVersions.AnyAsync(x => x.ConnectorId == id && x.State == "Published", ct))
            return Missing();
        if (connector.Lifecycle == "Retired") return Conflict("This connector is already retired.");
        connector.Lifecycle = input.State;
        if (input.State == "Retired") connector.Disabled = true;
        await Complete(db, http, input.State, id, tx, ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Change(EfiscalDbContext db, HttpContext http, Guid id, Guid versionId, int expected, string action,
        Func<ConnectorVersion, Connector, IResult?> update, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var connector = await Locked(db, id, ct);
        var version = await db.ConnectorVersions.FindAsync([versionId], ct);
        if (connector is null || version?.ConnectorId != id) return Missing();
        if (version.Revision != expected) return Conflict("Reload this version before making another change.");
        if (update(version, connector) is { } error) return error;
        version.Revision++;
        db.Audit("Connectors." + action, http.UserId(), target: version.Id);
        await Complete(db, http, action, id, tx, ct);
        return Results.Ok(Present(version, true));
    }

    private static async Task<Connector?> Locked(EfiscalDbContext db, Guid id, CancellationToken ct)
    {
        await db.Database.ExecuteSqlInterpolatedAsync($"""SELECT 1 FROM "Connectors" WHERE "Id" = {id} FOR UPDATE""", ct);
        return await db.Connectors.FindAsync([id], ct);
    }
    private static async Task Complete(EfiscalDbContext db, HttpContext http, string action, Guid id,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx, CancellationToken ct)
    {
        db.Audit("Connectors." + action, http.UserId(), target: id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    private static void Invalidate(ConnectorVersion version) { version.State = "Awaiting Validation"; version.Validation = null; }
    private static bool Editable(ConnectorVersion v) => v.State is "Draft Configuration" or "Awaiting Validation" or "Validated";
    private static IResult Missing() => ApiSupport.Problem(404, "The connector is unavailable.");
    private static IResult Conflict(string message) => ApiSupport.Problem(409, message);

    private static object Present(ConnectorVersion version, bool detail) => new
    {
        version.Id, version.ConnectorId, version.Connector.Code, version.Connector.Disabled, version.Connector.Lifecycle, version.Number, version.Revision, version.Category,
        version.Adapter, version.State, version.Availability, version.Configuration, version.CreatedAt, version.PublishedAt,
        effectiveAvailability = Availability(version),
        revocation = version.Revocation is { } revoked ? new { revoked.At, revoked.Reason, revoked.ActorId } : null,
        specification = detail ? version.Specification : null, validation = detail ? version.Validation : null
    };
}

