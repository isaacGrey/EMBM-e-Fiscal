using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record TemplateMappingInput(Guid ConnectorVersionId, string? Name, string DocumentType, string Direction, FieldRule[]? Rules);

internal static partial class VendorConnectEndpoints
{
    private static void MapVendorMapping(RouteGroupBuilder group)
    {
        group.MapGet("/mapping-templates", VendorTemplates).AddEndpointFilter(Grant(View));
        group.MapPost("/mapping-templates", PublishTemplate).AddEndpointFilter(Grant(Publish));
    }

    private static async Task<IResult> VendorTemplates(EfiscalDbContext db, CancellationToken ct) =>
        Results.Ok(new { items = await db.VendorMappingTemplates.AsNoTracking().OrderByDescending(x => x.PublishedAt).Take(100).ToArrayAsync(ct) });

    private static async Task<IResult> PublishTemplate(TemplateMappingInput input, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        var name = SpecImport.Text(input.Name, 90);
        Require(name != "" && DocumentRules.Types.Contains(input.DocumentType) && FieldMapper.Directions.Contains(input.Direction),
            "Select a supported document type and direction and supply a template name.");
        var connector = await db.ConnectorVersions.AsNoTracking().Include(x => x.Connector).SingleOrDefaultAsync(x =>
            x.Id == input.ConnectorVersionId && x.State == "Published" && !x.Connector.Disabled && x.Revocation == null, ct);
        if (connector is null || !IsMock(connector.Adapter)) return Missing();
        var rules = input.Rules ?? FieldMapper.Defaults(input.Direction);
        var errors = FieldMapper.Validate(input.Direction, rules);
        if (errors.Length > 0) return ApiSupport.Invalid("rules", errors[0]);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (await Locked(db, connector.ConnectorId, ct) is not { Disabled: false } ||
            await db.Set<ConnectorVersionRevocation>().AnyAsync(x => x.VersionId == connector.Id, ct)) return Missing();
        var latest = await db.VendorMappingTemplates.Where(x => x.ConnectorVersionId == input.ConnectorVersionId && x.Name == name &&
            x.DocumentType == input.DocumentType && x.Direction == input.Direction).MaxAsync(x => (int?)x.Version, ct) ?? 0;
        var row = new VendorMappingTemplate { ConnectorVersionId = input.ConnectorVersionId, Name = name, DocumentType = input.DocumentType,
            Direction = input.Direction, Version = latest + 1, Rules = Freeze(rules), Validation = Freeze(errors), PublishedBy = http.UserId() };
        db.VendorMappingTemplates.Add(row);
        await Complete(db, http, "MappingTemplatePublished", connector.ConnectorId, tx, ct);
        return Results.Created("/api/vendor/connectors/mapping-templates", row);
    }
}

