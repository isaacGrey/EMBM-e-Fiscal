using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ProfileInput(Guid ConnectionId, string? Name, string DocumentType, string Direction);
internal sealed record MappingInputVersion(int ExpectedVersion, FieldRule[]? Rules, Guid? TemplateId);
internal sealed record MappingActivation(int Version);
internal sealed record MappingPreview(FieldRule[]? Rules, Guid? DocumentId);
internal sealed record ReferenceInput(string Kind, string? LocalKey, string? ExternalId);

internal static partial class ClientConnectEndpoints
{
    private static void MapFieldMapping(RouteGroupBuilder group)
    {
        group.MapGet("/mapping-fields/{direction}", MappingFields).AddEndpointFilter(Access(Mapping));
        group.MapGet("/mapping-profiles", Profiles).AddEndpointFilter(Access(Mapping));
        group.MapPost("/mapping-profiles", AddProfile).AddEndpointFilter(Access(Mapping, true));
        group.MapGet("/mapping-profiles/{profile:guid}", ProfileDetail).AddEndpointFilter(Access(Mapping));
        group.MapPost("/mapping-profiles/{profile:guid}/versions", AddMappingVersion).AddEndpointFilter(Access(Mapping, true));
        group.MapPost("/mapping-profiles/{profile:guid}/activate", ActivateMapping).AddEndpointFilter(Access(Mapping, true));
        group.MapPost("/mapping-profiles/{profile:guid}/disable", DisableMapping).AddEndpointFilter(Access(Mapping, true));
        group.MapPost("/mapping-profiles/{profile:guid}/preview", PreviewMapping).AddEndpointFilter(Access(Mapping));
        group.MapGet("/connections/{id:guid}/references", ReferenceChoices).AddEndpointFilter(Access(Mapping));
        group.MapPut("/connections/{id:guid}/references", PutReference).AddEndpointFilter(Access(Mapping, true));
    }

    internal static IQueryable<MappingProfile> ScopedProfiles(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.MappingProfiles.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    private static async Task<MappingProfile?> FindProfile(EfiscalDbContext db, Guid subscriber, Guid company, Guid id, CancellationToken ct) =>
        await ScopedProfiles(db, subscriber, company).Include(x => x.Connection).ThenInclude(x => x.Version).ThenInclude(x => x.Connector)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
    internal static IQueryable<MappingVersion> Versions(EfiscalDbContext db, MappingProfile p) =>
        db.MappingVersions.Where(x => x.SubscriberId == p.SubscriberId && x.CompanyId == p.CompanyId && x.ProfileId == p.Id);

    private static IResult MappingFields(string direction)
    {
        Require(FieldMapper.Directions.Contains(direction), "Choose a supported mapping direction.");
        var fields = FieldMapper.Describe(direction);
        return Results.Ok(new { fields.Source, fields.Destination, operations = FieldMapper.Operations, defaults = FieldMapper.Defaults(direction) });
    }

    private static async Task<IResult> Profiles(Guid subscriber, Guid company, Guid? connection, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var records = await ScopedProfiles(db, subscriber, company).AsNoTracking().Include(x => x.Connection)
            .Where(x => connection == null || x.ConnectionId == connection).OrderBy(x => x.Name).Take(100).ToArrayAsync(ct);
        return Results.Ok(new { items = records.Select(ProfileView) });
    }

    private static async Task<IResult> AddProfile(Guid subscriber, Guid company, ProfileInput input, HttpContext http, EfiscalDbContext db,
        ConnectionRuntime runtime, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, input.ConnectionId, ct);
        if (link is null || !MockAccess(http) || !IsMock(link.Version.Adapter)) return TenantAccess.Missing();
        var name = SpecImport.Text(input.Name, 80);
        Require(DocumentRules.Types.Contains(input.DocumentType) && FieldMapper.Directions.Contains(input.Direction) && name != "",
            "Enter a profile name and select a supported document type and direction.");
        Require(input.Direction == FieldMapper.Inbound || link.ResourceType == "CompanyFile", "Use an accounting connection for outbound and response mappings.");
        if (await ScopedProfiles(db, subscriber, company).AnyAsync(x => x.ConnectionId == input.ConnectionId && x.DocumentType == input.DocumentType
            && x.Direction == input.Direction && x.Name == name, ct)) return Conflict("That mapping profile already exists.");
        var profile = new MappingProfile { SubscriberId = subscriber, CompanyId = company, Connection = link, ConnectionId = link.Id,
            Name = name, Direction = input.Direction, DocumentType = input.DocumentType, Operation = FieldMapper.Operation(input.DocumentType, input.Direction) };
        db.Add(profile);
        db.Audit("Mappings.ProfileCreated", http.UserId(), subscriber, profile.Id);
        return Results.Created("/api/workspace/" + subscriber + "/companies/" + company + "/connect/mapping-profiles/" + profile.Id, ProfileView(profile));
    }

    private static async Task<IResult> ProfileDetail(Guid subscriber, Guid company, Guid profile, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, profile, ct) is not { } p) return TenantAccess.Missing();
        var versions = await Versions(db, p).AsNoTracking().OrderByDescending(x => x.Number).Take(30).ToArrayAsync(ct);
        var templates = await db.VendorMappingTemplates.AsNoTracking().Where(x => x.ConnectorVersionId == p.Connection.ConnectorVersionId &&
            x.DocumentType == p.DocumentType && x.Direction == p.Direction).OrderByDescending(x => x.Version).Take(40)
            .Select(x => new { x.Id, x.Name, x.Version }).ToArrayAsync(ct);
        var text = FieldMapper.Describe(p.Direction);
        return Results.Ok(new { profile = ProfileView(p), versions = versions.Select(v => VersionView(v, p)), templates,
            fields = text, operations = FieldMapper.Operations, defaults = FieldMapper.Defaults(p.Direction) });
    }

    private static async Task<IResult> AddMappingVersion(Guid subscriber, Guid company, Guid profile, MappingInputVersion input,
        EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, profile, ct) is not { } p) return TenantAccess.Missing();
        var latest = await Versions(db, p).MaxAsync(x => (int?)x.Number, ct) ?? 0;
        if (latest != input.ExpectedVersion) return Conflict("Reload the latest mapping version before saving.");
        var rules = input.Rules;
        if (input.TemplateId is Guid template)
        {
            var approved = await db.VendorMappingTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Id == template &&
                x.ConnectorVersionId == p.Connection.ConnectorVersionId && x.DocumentType == p.DocumentType && x.Direction == p.Direction, ct);
            if (approved is null) return TenantAccess.Missing();
            rules = approved.Rules.Deserialize<FieldRule[]>(SpecImport.Json);
        }
        Require(rules is not null, "Supply mapping rules or an approved vendor template.");
        var errors = FieldMapper.Validate(p.Direction, rules!);
        var references = await PinnedReferences(db, p, ct);
        var row = new MappingVersion { SubscriberId = subscriber, CompanyId = company, ProfileId = profile,
            Number = latest + 1, ConnectorVersionId = p.Connection.ConnectorVersionId, Rules = Freeze(rules), ReferenceSnapshot = Freeze(references),
            Validation = Freeze(errors), Validated = errors.Length == 0, ActorId = http.UserId() };
        db.MappingVersions.Add(row);
        db.Audit("Mappings.VersionCreated", http.UserId(), subscriber, profile);
        return Results.Ok(VersionView(row, p));
    }

    private static async Task<IResult> ActivateMapping(Guid subscriber, Guid company, Guid profile, MappingActivation input,
        EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, profile, ct) is not { } p) return TenantAccess.Missing();
        var version = await Versions(db, p).SingleOrDefaultAsync(x => x.Number == input.Version, ct);
        if (version is null || !version.Validated) return Conflict("Validate this mapping version before activating it.");
        if (p.Direction != FieldMapper.Outbound && await ScopedProfiles(db, subscriber, company).AnyAsync(x => x.Id != p.Id &&
            x.ConnectionId == p.ConnectionId && x.DocumentType == p.DocumentType && x.Direction == p.Direction && x.ActiveVersion != null && !x.Disabled, ct))
            return Conflict("Disable the current active mapping for this connection, document and direction before activating a different profile.");
        version.ActivatedAt ??= DateTimeOffset.UtcNow;
        p.ActiveVersion = version.Number;
        p.Disabled = false;
        db.Audit("Mappings.Activated", http.UserId(), subscriber, profile);
        return Results.Ok(ProfileView(p));
    }

    private static async Task<IResult> DisableMapping(Guid subscriber, Guid company, Guid profile, EfiscalDbContext db,
        HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, profile, ct) is not { } p) return TenantAccess.Missing();
        p.Disabled = true;
        db.Audit("Mappings.Disabled", http.UserId(), subscriber, profile);
        return Results.Ok(ProfileView(p));
    }

    private static async Task<IResult> PreviewMapping(Guid subscriber, Guid company, Guid profile, MappingPreview input, int? version, EfiscalDbContext db,
        HttpContext http, CancellationToken ct)
    {
        if (!MockAccess(http) || await FindProfile(db, subscriber, company, profile, ct) is not { } p) return TenantAccess.Missing();
        var saved = version.HasValue ? await Versions(db, p).AsNoTracking().SingleOrDefaultAsync(x => x.Number == version, ct) : null;
        if (version.HasValue && saved is null) return TenantAccess.Missing();
        var rules = input.Rules ?? saved?.Rules.Deserialize<FieldRule[]>(SpecImport.Json) ?? FieldMapper.Defaults(p.Direction);
        var errors = FieldMapper.Validate(p.Direction, rules);
        if (errors.Length > 0) return Results.Ok(new { valid = false, errors });
        var source = FieldMapper.Sample(p.DocumentType, p.Direction);
        if (input.DocumentId is Guid id)
        {
            if (p.Direction != FieldMapper.Outbound || await DocumentEndpoints.Access(subscriber, company,
                DocumentRules.Permission(p.DocumentType, false), http, db, ct) is null) return TenantAccess.Missing();
            var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Snapshot).Include(x => x.Lines).SingleOrDefaultAsync(
                x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id && x.Type == p.DocumentType, ct);
            if (doc is null) return TenantAccess.Missing();
            source = FieldMapper.Document(doc);
        }
        var references = saved?.ReferenceSnapshot.Deserialize<BoundReference[]>(SpecImport.Json) ?? await PinnedReferences(db, p, ct);
        if (!input.DocumentId.HasValue)
        {
            var fixture = FieldMapper.Describe(p.Direction).Source.Where(field => field.Reference is not null)
                .SelectMany(field => (field.Scope == "Header" ? [source.Header] : source.Lines)
                    .Select(row => row.GetValueOrDefault(field.Code)?.ToString())
                    .Where(value => !string.IsNullOrEmpty(value)).Select(value => new BoundReference(field.Reference!, value!, value!, "SAMPLE")))
                .DistinctBy(x => (x.Kind, x.LocalKey)).ToArray();
            references = fixture;
        }
        try
        {
            var payload = FieldMapper.Map(p.Direction, rules, source, references);
            return Results.Ok(new { valid = true, errors, source = Sanitized(source), payload = Sanitized(payload),
                request = new { operation = p.Operation, execution = "Preview only" }, expectedResponse = FieldMapper.Sample(p.DocumentType, FieldMapper.Response) });
        }
        catch (ConnectorInputError error) { return Results.Ok(new { valid = false, errors = new[] { error.Message } }); }
    }

    private static MappingValues Sanitized(MappingValues data) => new(Clean(data.Header), data.Lines.Select(Clean).ToArray());
    private static Dictionary<string, object?> Clean(Dictionary<string, object?> row) => row.ToDictionary(x => x.Key,
        x => x.Key is "remarks" or "description" or "customLine" or "customHeader" or "customer" or "item" ? "[redacted]" : x.Value);

    internal static readonly Dictionary<string, string> ReferenceKinds = new()
    {
        ["Customer"] = "customers", ["Item"] = "items", ["Tax"] = "tax-groups", ["Discount"] = "discount-groups", ["Unit"] = "units",
        ["Currency"] = "currencies", ["PaymentTerm"] = "payment-terms", ["Account"] = "dimensions", ["Project"] = "dimensions",
        ["Department"] = "dimensions", ["Location"] = "dimensions", ["Custom"] = "custom-fields"
    };
    internal static BoundReference[] SandboxReferences(string kind) => kind switch
    {
        "Customer" => [new(kind, "", "DEMO-BUYER", "DEMO-BUYER")],
        "Item" => [new(kind, "", "DEMO-SERVICE", "DEMO-SERVICE")],
        "Tax" => [new(kind, "", "VAT12", "VAT12")],
        "Discount" => [new(kind, "", "PROMO5", "PROMO5")],
        "Unit" => [new(kind, "", "EA", "EA")],
        "Currency" => [new(kind, "", "PHP", "PHP")],
        "PaymentTerm" => [new(kind, "", "NET30", "NET30")],
        "Account" => [new(kind, "", "REV", "REV")],
        "Project" => [new(kind, "", "PRJ-1", "PRJ-1")],
        "Department" => [new(kind, "", "MNL", "MNL")],
        "Location" => [new(kind, "", "HQ", "HQ")],
        "Custom" => [new(kind, "", "CUSTOM", "CUSTOM")],
        _ => []
    };
    private static async Task<IResult> ReferenceChoices(Guid subscriber, Guid company, Guid id, string? kind, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !MockAccess(http) || link.Status != "Connected") return TenantAccess.Missing();
        var type = kind ?? "Customer";
        Require(ReferenceKinds.TryGetValue(type, out var catalog), "Choose a supported reference category.");
        var references = SandboxReferences(type);
        var records = await db.MasterRecords.AsNoTracking().Where(m => m.SubscriberId == subscriber && m.CompanyId == company && m.Kind == catalog && m.Active)
            .OrderBy(x => x.Code).Take(250).ToArrayAsync(ct);
        var scoped = records.Where(x => catalog != "dimensions" || SpecImport.Str(x.Data, "dimensionType") == Dimension(type))
            .Select(x => new { x.Id, x.Code, x.Name }).ToArray();
        var saved = await ReadReferences(db, subscriber, company, id, ct);
        return Results.Ok(new { kinds = ReferenceKinds.Keys, kind = type, choices = references, local = scoped, items = saved.Where(x => x.Kind == type),
            suggestions = scoped.SelectMany(x => references.Where(r => r.ExternalCode.Equals(x.Code, StringComparison.OrdinalIgnoreCase) &&
                !saved.Any(s => s.Kind == type && (s.LocalKey == x.Id.ToString() || s.ExternalId == r.ExternalId))).Select(r => new { x.Id, r.ExternalId })) });
    }

    private static async Task<IResult> PutReference(Guid subscriber, Guid company, Guid id, ReferenceInput input, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !MockAccess(http) || link.Status != "Connected") return TenantAccess.Missing();
        if (!ReferenceKinds.TryGetValue(input.Kind ?? "", out var kind) ||
            SandboxReferences(input.Kind!).SingleOrDefault(x => x.ExternalId == input.ExternalId) is not { } external) return TenantAccess.Missing();
        var local = input.LocalKey ?? "";
        Require((input.Kind == "Currency" && local == "PHP") || Guid.TryParse(local, out _), "Select an active company reference.");
        if (Guid.TryParse(local, out var key))
        {
            var selected = await db.MasterRecords.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
                x.Kind == kind && x.Id == key && x.Active, ct);
            if (selected is null || kind == "dimensions" && SpecImport.Str(selected.Data, "dimensionType") != Dimension(input.Kind))
                return TenantAccess.Missing();
        }
        if (Guid.TryParse(local, out key)) local = key.ToString();
        var scope = db.ReferenceMappings.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == id && x.Kind == input.Kind);
        if (await scope.AnyAsync(x => x.ExternalId == external.ExternalId && x.LocalKey != local, ct)) return Conflict("That external identity is already mapped.");
        var row = await scope.SingleOrDefaultAsync(x => x.LocalKey == local, ct);
        if (row is null)
        {
            if (await db.ReferenceMappings.CountAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == id, ct) >= 1500)
                return Conflict("The connection reference limit has been reached.");
            row = new ReferenceMapping { SubscriberId = subscriber, CompanyId = company, ConnectionId = id, Kind = input.Kind!, LocalKey = local };
            db.ReferenceMappings.Add(row);
        }
        row.ExternalId = external.ExternalId;
        row.ExternalCode = external.ExternalCode;
        row.ExternalName = external.ExternalCode;
        db.Audit("Mappings.ReferenceSaved", http.UserId(), subscriber, id);
        return Results.Ok(new { row.Kind, row.LocalKey, row.ExternalId, row.ExternalCode });
    }

    private static async Task<BoundReference[]> PinnedReferences(EfiscalDbContext db, MappingProfile profile, CancellationToken ct)
    {
        var manual = await ReadReferences(db, profile.SubscriberId, profile.CompanyId, profile.ConnectionId, ct);
        if (profile.Direction != FieldMapper.Outbound || profile.Connection.MappingVersion is not int revision) return manual;
        var bootstrap = await db.ConnectionMappings.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == profile.SubscriberId &&
            x.CompanyId == profile.CompanyId && x.ConnectionId == profile.ConnectionId && x.Number == revision, ct);
        if (bootstrap is null) return manual;
        BoundReference[] fixtures = [Legacy("Customer", bootstrap.CustomerId), Legacy("Item", bootstrap.ItemId), Legacy("Unit", bootstrap.UnitId)];
        return [..manual, ..fixtures.Where(x => !manual.Any(m => m.Kind == x.Kind && m.LocalKey == x.LocalKey))];
        static BoundReference Legacy(string kind, Guid id) => new(kind, id.ToString(), id.ToString(), "Development binding");
    }

    private static string Dimension(string? name) => name == "Account" ? "AccountReference" : name ?? "";

    internal static Task<BoundReference[]> ReadReferences(EfiscalDbContext db, Guid subscriber, Guid company, Guid connection, CancellationToken ct) =>
        db.ReferenceMappings.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ConnectionId == connection)
            .OrderBy(x => x.Kind).ThenBy(x => x.LocalKey)
            .Select(x => new BoundReference(x.Kind, x.LocalKey, x.ExternalId, x.ExternalCode)).ToArrayAsync(ct);

    private static object ProfileView(MappingProfile p) => new { p.Id, p.ConnectionId, connectionName = p.Connection.Name, p.Name, p.DocumentType,
        p.Direction, p.Operation, p.ActiveVersion, p.Disabled, p.Connection.ConnectorVersionId };
    private static object VersionView(MappingVersion v, MappingProfile p) => new { v.Number, v.Rules, v.Validation, v.CreatedAt,
        v.ConnectorVersionId, status = v.Number == p.ActiveVersion ? p.Disabled ? "Disabled" : "Active" :
            v.ActivatedAt.HasValue ? "Superseded" : v.Validated ? "Validated" : "Validation Required" };
}

