using System.Data;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record MasterScope(Guid SubscriberId, Guid CompanyId, TenantGrant Grant);

internal static class MasterEndpoints
{
    public static void MapMasterData(this WebApplication app)
    {
        var routes = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/master-data")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Master data");
        routes.MapGet("/", Menu);
        routes.MapGet("/{kind}", List);
        routes.MapGet("/{kind}/lookup", Lookup);
        routes.MapGet("/{kind}/{id:guid}", Detail);
        routes.MapPost("/{kind}", Create);
        routes.MapPut("/{kind}/{id:guid}", Update);
        routes.MapGet("/{kind}/file", Download);
        routes.MapPost("/{kind}/import/{action}", Import).DisableAntiforgery(); // The route group enforces the SPA antiforgery header.
        routes.MapPost("/calculate", Calculate);
        routes.MapPost("/resolve-price", Quote);
    }

    internal static async Task<MasterScope?> Authorize(Guid subscriber, Guid company, string permission, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        var grant = await TenantAccess.ResolveAsync(db, http, subscriber, permission, ct);
        if (grant is null || !await TenantAccess.Companies(db, subscriber, http.UserId(), grant).AnyAsync(x => x.Id == company, ct)) return null;
        return new(subscriber, company, grant);
    }

    private static MasterDefinition? Definition(string kind) => MasterSchema.Definitions.SingleOrDefault(x => x.Key == kind);

    private static async Task<IResult> Menu(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var access = await Authorize(subscriber, company, MasterSchema.View, http, db, ct);
        if (access is null) return TenantAccess.Missing();
        var grants = access.Grant.Grants;
        var visible = MasterSchema.Definitions.Where(d => grants.Contains(d.Read)).Select(d => new
        {
            d.Key, d.Label, d.Fields, effective = d.Key is "tax-groups" or "discount-groups", canWrite = grants.Contains(d.Write),
            canImport = d.Import && grants.Contains(MasterSchema.ImportPermission(d.Key)), canExport = grants.Contains(MasterSchema.Export)
        });
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        var rdos = await db.RdoReferences.AsNoTracking().Where(x => x.Active && (development || !x.DevelopmentFixture))
            .OrderBy(x => x.Code).Select(x => new { x.Code, x.Name }).ToArrayAsync(ct);
        var customFields = (await db.MasterRecords.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.Kind == "custom-fields" && x.Active).ToArrayAsync(ct)).OrderBy(x => x.Data.Num("displayOrder")).ThenBy(x => x.Code)
            .Select(x => ToRow(x, Definition(x.Kind)!));
        return Results.Ok(new { definitions = visible, rdos, regulatoryReferences = RegulatoryCatalog.Rules,
            customFields, recordTypes = MasterSchema.RecordTypes, canCalculate = grants.Contains(MasterSchema.Calculate) });
    }

    private static async Task<IResult> List(Guid subscriber, Guid company, string kind, string? q, bool? active, string? filter,
        string? sort, bool? descending, int? page, HttpContext http, EfiscalDbContext db, MasterRegistry registry, CancellationToken ct)
    {
        var definition = Definition(kind);
        if (definition is null || await Authorize(subscriber, company, definition.Read, http, db, ct) is null) return TenantAccess.Missing();
        if (!ApiSupport.Optional(q, 120) || !ApiSupport.Optional(filter, 80)) return ApiSupport.Invalid("q", "Use a shorter search or filter.");
        var query = Search(registry.Scope(subscriber, company).AsNoTracking().Where(x => x.Kind == kind), q, active);
        if (!string.IsNullOrEmpty(filter))
        {
            var field = definition.Fields.FirstOrDefault(f => f.Type == "choice");
            if (field is null || !field.Choices!.Contains(filter)) return ApiSupport.Invalid("filter", "Choose an available filter.");
            var json = JsonSerializer.Serialize(new Dictionary<string, string> { [field.Key] = filter });
            query = query.Where(x => EF.Functions.JsonContains(x.Data, json));
        }
        var number = Math.Clamp(page ?? 1, 1, 100_000);
        var total = await query.CountAsync(ct);
        var rows = await Order(query, sort, descending == true).ThenBy(x => x.Id).Skip((number - 1) * 25).Take(25).ToArrayAsync(ct);
        return Results.Ok(new { items = rows.Select(x => ToRow(x, definition)), total, page = number, pageSize = 25 });
    }

    private static async Task<IResult> Lookup(Guid subscriber, Guid company, string kind, string? q, Guid? include,
        HttpContext http, EfiscalDbContext db, MasterRegistry registry, CancellationToken ct)
    {
        var definition = Definition(kind);
        if (definition is null || await Authorize(subscriber, company, definition.Read, http, db, ct) is null) return TenantAccess.Missing();
        if (!ApiSupport.Optional(q, 120)) return ApiSupport.Invalid("q", "Use a shorter search.");
        var query = registry.Scope(subscriber, company).AsNoTracking().Where(x => x.Kind == kind);
        var items = await Search(query, q, true).OrderBy(x => x.Code).Take(50).Select(x => new { x.Id, x.Code, x.Name, x.Active }).ToListAsync(ct);
        if (include is Guid selected && items.All(x => x.Id != selected))
        {
            var existing = await query.Where(x => x.Id == selected).Select(x => new { x.Id, x.Code, x.Name, x.Active }).SingleOrDefaultAsync(ct);
            if (existing is not null) items.Insert(0, existing);
        }
        return Results.Ok(items);
    }

    private static async Task<IResult> Detail(Guid subscriber, Guid company, string kind, Guid id, HttpContext http, EfiscalDbContext db,
        MasterRegistry registry, CancellationToken ct)
    {
        var definition = Definition(kind);
        if (definition is null || await Authorize(subscriber, company, definition.Read, http, db, ct) is null) return TenantAccess.Missing();
        var row = await registry.Scope(subscriber, company).AsNoTracking().SingleOrDefaultAsync(x => x.Kind == kind && x.Id == id, ct);
        if (row is null) return TenantAccess.Missing();
        var history = await db.MasterRevisions.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.RecordId == id)
            .OrderByDescending(x => x.Revision).Take(100).ToArrayAsync(ct);
        return Results.Ok(new { record = ToRow(row, definition), history = history.Select(x => new { x.Revision, x.Name, x.Active,
            x.EffectiveFrom, x.EffectiveThrough, x.ActorId, x.At, x.Action, data = MasterRegistry.SafeData(x.Data) }) });
    }

    private static Task<IResult> Create(Guid subscriber, Guid company, string kind, MasterInput input, HttpContext http,
        EfiscalDbContext db, MasterRegistry registry, CancellationToken ct) =>
        Write(subscriber, company, kind, null, input, http, db, registry, ct);

    private static Task<IResult> Update(Guid subscriber, Guid company, string kind, Guid id, MasterInput input, HttpContext http,
        EfiscalDbContext db, MasterRegistry registry, CancellationToken ct) =>
        Write(subscriber, company, kind, id, input, http, db, registry, ct);

    private static async Task<IResult> Write(Guid subscriber, Guid company, string kind, Guid? id, MasterInput input, HttpContext http,
        EfiscalDbContext db, MasterRegistry registry, CancellationToken ct)
    {
        var definition = Definition(kind);
        if (definition is null || await Authorize(subscriber, company, definition.Write, http, db, ct) is null) return TenantAccess.Missing();
        await using var transaction = await db.LockSubscriberAsync(subscriber, ct);
        if (await Authorize(subscriber, company, definition.Write, http, db, ct) is null) return TenantAccess.Missing();
        var record = id is null ? null : await registry.Scope(subscriber, company).SingleOrDefaultAsync(x => x.Kind == kind && x.Id == id, ct);
        if (id is not null && record is null) return TenantAccess.Missing();
        try
        {
            var action = record is null ? "Created" : record.Active != input.Active ? input.Active ? "Activated" : "Deactivated" : "Updated";
            var saved = await registry.Write(subscriber, company, http.UserId(), definition, input, record,
                http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment(), action, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return record is null ? Results.Created($"/api/workspace/{subscriber}/companies/{company}/master-data/{kind}/{saved.Id}", ToRow(saved, definition))
                : Results.Ok(ToRow(saved, definition));
        }
        catch (MasterValidation error) { return Invalid(error); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error))
        { return ApiSupport.Problem(409, "A conflicting master-data record already exists."); }
    }

    private static async Task<IResult> Download(Guid subscriber, Guid company, string kind, string? format, bool? template, HttpContext http,
        EfiscalDbContext db, MasterRegistry registry, CancellationToken ct)
    {
        var definition = Definition(kind);
        if (definition is not { Import: true } || await Authorize(subscriber, company, definition.Read, http, db, ct) is not { } access)
            return TenantAccess.Missing();
        var grant = template == true ? MasterSchema.ImportPermission(kind) : MasterSchema.Export;
        if (!access.Grant.Grants.Contains(grant)) return TenantAccess.Missing();
        if (format is not ("csv" or "xlsx")) return ApiSupport.Invalid("format", "Choose CSV or XLSX.");
        var rows = template == true ? [] : await registry.Scope(subscriber, company).AsNoTracking()
            .Where(x => x.Kind == kind).OrderBy(x => x.Code).Take(MasterImport.MaxRecords + 1).ToArrayAsync(ct);
        if (rows.Length > MasterImport.MaxRecords) return ApiSupport.Invalid("export", "This export exceeds the 1,000 record limit.");
        return MasterImport.Download(definition, rows, format, template == true);
    }

    private static async Task<IResult> Import(Guid subscriber, Guid company, string kind, string action, HttpContext http,
        EfiscalDbContext db, MasterImport handler, CancellationToken ct)
    {
        var definition = Definition(kind);
        var permission = MasterSchema.ImportPermission(kind);
        if (definition is not { Import: true } || action is not ("preview" or "confirm" or "errors") ||
            await Authorize(subscriber, company, permission, http, db, ct) is null) return TenantAccess.Missing();
        if (!http.Request.HasFormContentType || http.Request.ContentLength is > MasterImport.MaxFileBytes + 32768)
            return ApiSupport.Invalid("file", "Upload one CSV or XLSX file, up to 2 MB.");
        try
        {
            var form = await http.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || form.Files.Count != 1 || file.Length is <= 0 or > MasterImport.MaxFileBytes)
                return ApiSupport.Invalid("file", "Upload one CSV or XLSX file, up to 2 MB.");
            return await handler.Handle(subscriber, company, definition, action, file, form["previewToken"].ToString(), http, ct);
        }
        catch (MasterValidation error) { return Invalid(error); }
        catch (InvalidDataException) { return ApiSupport.Invalid("file", "The file is invalid or exceeds the spreadsheet limits."); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error))
        { return ApiSupport.Problem(409, "A conflicting master-data record already exists."); }
    }

    private static Task<IResult> Calculate(Guid subscriber, Guid company, PreviewInput input, HttpContext http,
        EfiscalDbContext db, FinancialPreview service, CancellationToken ct) =>
        Preview(subscriber, company, http, db, () => service.Calculate(subscriber, company, input, ct), ct);

    private static Task<IResult> Quote(Guid subscriber, Guid company, PriceInput input, HttpContext http,
        EfiscalDbContext db, FinancialPreview service, CancellationToken ct) =>
        Preview(subscriber, company, http, db, () => service.Quote(subscriber, company, input, ct), ct);

    private static async Task<IResult> Preview(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db,
        Func<Task<object>> execute, CancellationToken ct)
    {
        if (await Authorize(subscriber, company, MasterSchema.Calculate, http, db, ct) is null) return TenantAccess.Missing();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        if (await Authorize(subscriber, company, MasterSchema.Calculate, http, db, ct) is null) return TenantAccess.Missing();
        try { return Results.Ok(await execute()); }
        catch (MasterValidation error) { return Invalid(error); }
        catch (OverflowException) { return ApiSupport.Invalid("amount", "The financial inputs exceed supported decimal limits."); }
    }

    private static IResult Invalid(MasterValidation error) =>
        error.Field == "revision" ? ApiSupport.Problem(409, error.Message) : ApiSupport.Invalid(error.Field, error.Message);

    private static object ToRow(MasterRecord x, MasterDefinition definition) => new { x.Id, x.Code, x.Name, x.Active, x.Revision, x.UpdatedAt,
        data = MasterRegistry.SafeData(x.Data),
        verificationStatus = definition.Key is "tax-groups" or "discount-groups" ? RegulatoryCatalog.Status(definition.Key, x.Data) : null };

    private static IQueryable<MasterRecord> Search(IQueryable<MasterRecord> query, string? search, bool? active)
    {
        if (active.HasValue) query = query.Where(x => x.Active == active);
        if (string.IsNullOrWhiteSpace(search)) return query;
        var q = search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
        return query.Where(x => EF.Functions.ILike(x.Code, $"%{q}%", @"\") || EF.Functions.ILike(x.Name, $"%{q}%", @"\"));
    }

    private static IOrderedQueryable<MasterRecord> Order(IQueryable<MasterRecord> query, string? sort, bool descending) => (sort, descending) switch
    {
        ("name", true) => query.OrderByDescending(x => x.Name),
        ("name", false) => query.OrderBy(x => x.Name),
        ("active", true) => query.OrderByDescending(x => x.Active).ThenBy(x => x.Code),
        ("active", false) => query.OrderBy(x => x.Active).ThenBy(x => x.Code),
        ("updatedAt", true) => query.OrderByDescending(x => x.UpdatedAt),
        ("updatedAt", false) => query.OrderBy(x => x.UpdatedAt),
        (_, true) => query.OrderByDescending(x => x.Code),
        _ => query.OrderBy(x => x.Code)
    };
}

