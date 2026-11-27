using System.Data;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal record DocumentAccess(Company Company, TenantGrant Grant);
internal static partial class DocumentEndpoints
{
    public const string ExportPermission = "Documents.Export";
    public const string ResendPermission = "Documents.Resend";

    public static void MapDocuments(this WebApplication app)
    {
        var group = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/documents")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("Committed documents");
        group.MapGet("/numbering", Numbering);
        group.MapPost("/numbering/{type}", PublishNumbering);
        group.MapGet("/external-order/{key}", ExternalSource);
        group.MapGet("/commit-result/{key}", RecoverCommit);
        group.MapGet("/{type}/options", Options);
        group.MapGet("/{type}/register", Register);
        group.MapGet("/{type}/{id:guid}", Detail);
        group.MapGet("/{type}/{id:guid}/source-lines", SourceLines);
        group.MapPost("/{type}/preview", Preview);
        group.MapPost("/{type}/commit", Commit);
        CanonicalInvoices.MapCanonicalInvoices(group);
        MapDelivery(group, app.Environment.IsDevelopment());
    }

    internal static async Task<DocumentAccess?> Access(Guid subscriber, Guid company, string permission, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        var grant = await TenantAccess.ResolveAsync(db, http, subscriber, permission, ct);
        if (grant is null) return null;
        var workspace = await TenantAccess.Companies(db, subscriber, http.UserId(), grant).AsNoTracking().SingleOrDefaultAsync(x => x.Id == company, ct);
        return workspace is null ? null : new(workspace, grant);
    }

    private static async Task<IResult> Options(Guid subscriber, Guid company, string type, HttpContext http, EfiscalDbContext db,
        DocumentCommitService service, CancellationToken ct)
    {
        if (!Types.Contains(type) || await Access(subscriber, company, Permission(type, false), http, db, ct) is not { } scope)
            return TenantAccess.Missing();
        var typeName = TemplateType(type);
        var templates = SalesOrder(type) ? [] : await db.CompanyTemplates.AsNoTracking()
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentType == typeName)
            .OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Kind, x.IsDefault, x.CurrentVersion }).ToArrayAsync(ct);
        var branches = await db.Branches.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .OrderBy(x => x.Code).Select(x => new { x.Id, x.Code, x.Name }).ToArrayAsync(ct);
        var canWrite = scope.Grant.Grants.Contains(Permission(type, true));
        return Results.Ok(new { canWrite, canCommit = canWrite && service.CanCommit(type, scope.Grant),
            canExport = scope.Grant.Grants.Contains(ExportPermission), canResend = scope.Grant.Grants.Contains(ResendPermission),
            development = service.LocalFixture(scope.Grant), canBuyCredits = scope.Grant.Grants.Contains("Credits.Purchase"),
            availableCredits = SalesOrder(type) || !canWrite ? (long?)null : await service.Balance(subscriber, ct), templates, branches });
    }

    private static async Task<IResult> Preview(Guid subscriber, Guid company, string type, DocumentInput input, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct)
    {
        if (!Types.Contains(type)) return TenantAccess.Missing();
        return await ReadPreview(subscriber, company, type, http, db, async scope => Results.Ok(DocumentDtos.Preview(
            await assembly.Prepare(scope.Company, http.UserId(), type, input, service.LocalFixture(scope.Grant), ct),
            SalesOrder(type) ? null : await service.Balance(subscriber, ct))), ct);
    }

    internal static async Task<IResult> ReadPreview(Guid subscriber, Guid company, string type, HttpContext http, EfiscalDbContext db,
        Func<DocumentAccess, Task<IResult>> execute, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        if (await Access(subscriber, company, Permission(type, true), http, db, ct) is not { } scope) return TenantAccess.Missing();
        try { return await execute(scope); }
        catch (MasterValidation error) { return Invalid(error); }
        catch (OverflowException) { return ApiSupport.Invalid("amount", "The document exceeds supported decimal limits."); }
    }

    private static async Task<IResult> Commit(Guid subscriber, Guid company, string type, CommitInput input, HttpContext http, EfiscalDbContext db,
        DocumentCommitService service, CancellationToken ct)
    {
        if (!Types.Contains(type) || await Access(subscriber, company, Permission(type, true), http, db, ct) is null)
            return TenantAccess.Missing();
        try
        {
            var doc = await service.Commit(subscriber, company, type, input, http, ct);
            return doc is null ? TenantAccess.Missing() : Results.Ok(DocumentDtos.Receipt(doc));
        }
        catch (MasterValidation error) { return Invalid(error); }
        catch (CommercialConflict error) { return ApiSupport.Problem(409, error.Message); }
        catch (CommitmentConflict error) { return ApiSupport.Problem(409, error.Message); }
        catch (OverflowException) { return ApiSupport.Invalid("amount", "The document exceeds supported decimal limits."); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error))
        { return ApiSupport.Problem(409, "The number or operation conflicts with a committed document. Reload the current settings."); }
    }

    private static async Task<IResult> RecoverCommit(Guid subscriber, Guid company, string key, HttpContext http, EfiscalDbContext db,
        DocumentCommitService service, DocumentRelationships documents, CancellationToken ct)
    {
        try { Key(key); }
        catch (MasterValidation error) { return Invalid(error); }
        if (await Access(subscriber, company, Permissions.CompanyView, http, db, ct) is null) return TenantAccess.Missing();
        var previous = await service.Prior(subscriber, company, http.UserId(), key, ct);
        if (previous is null) return TenantAccess.Missing();
        var doc = await documents.Scope(subscriber, company).AsNoTracking().SingleAsync(x => x.Id == previous.DocumentId, ct);
        return await Access(subscriber, company, Permission(doc.Type, false), http, db, ct) is null
            ? TenantAccess.Missing() : Results.Ok(DocumentDtos.Receipt(doc));
    }

    private static async Task<IResult> Numbering(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db,
        DocumentSettings settings, CancellationToken ct)
    {
        if (await Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is null) return TenantAccess.Missing();
        var series = await settings.Series(subscriber, company).AsNoTracking().OrderBy(x => x.Type).ThenByDescending(x => x.Revision).Take(250)
            .Select(x => new { x.Id, x.Type, x.BranchId, x.EffectiveFrom, x.Prefix, x.Digits, x.Revision, x.NextNumber, x.InitialNumber }).ToArrayAsync(ct);
        var branches = await db.Branches.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company)
            .Select(x => new { x.Id, x.Code, x.Name }).ToArrayAsync(ct);
        return Results.Ok(new { series, branches });
    }

    private static async Task<IResult> PublishNumbering(Guid subscriber, Guid company, string type, SeriesInput input, HttpContext http,
        EfiscalDbContext db, DocumentSettings settings, CancellationToken ct)
    {
        if (!Types.Contains(type) || await Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is null) return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is null) return TenantAccess.Missing();
        try
        {
            var row = await settings.AddSeries(subscriber, company, http.UserId(), type, input, ct);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Ok(new { row.Id, row.Revision, row.Prefix, row.EffectiveFrom, row.NextNumber });
        }
        catch (MasterValidation error) { return Invalid(error); }
    }

    private static async Task<IResult> ExternalSource(Guid subscriber, Guid company, string key, HttpContext http, EfiscalDbContext db,
        DocumentRelationships documents, DocumentCommitService service, CancellationToken ct)
    {
        if (await Access(subscriber, company, Permission("SalesInvoice", true), http, db, ct) is not { } scope || !service.LocalFixture(scope.Grant))
            return TenantAccess.Missing();
        try
        {
            var order = (await documents.External(subscriber, company, key, ct))!;
            var used = await documents.AllocatedExternal(subscriber, company, order.Key, ct);
            return Results.Ok(new { order.Key, order.Number, order.Connector, order.Account, order.RecordId, order.SourceAt, order.CustomerId, order.Currency, order.BranchId, order.FinancialReview,
                lines = order.Lines.Select(x => new { x.Key, x.ItemId, x.UnitId, quantity = Money(x.Quantity),
                    remainingQuantity = Money((x.Quantity * x.Factor - used.GetValueOrDefault(x.Key)) / x.Factor) }), development = true });
        }
        catch (MasterValidation error) { return Invalid(error); }
    }

    private static async Task<IResult> SourceLines(Guid subscriber, Guid company, string type, Guid id, HttpContext http,
        EfiscalDbContext db, DocumentRelationships documents, CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "SalesOrder") || await Access(subscriber, company, Permission(type, false), http, db, ct) is null)
            return TenantAccess.Missing();
        try
        {
            var doc = (await documents.Source(subscriber, company, id, type, ct))!;
            var used = await documents.Allocated(subscriber, company, doc.Lines.Select(x => x.Id).ToArray(), ct);
            return Results.Ok(new { document = DocumentDtos.Receipt(doc), doc.PartialFulfillment, lines = doc.Lines.OrderBy(x => x.Position).Select(x =>
            {
                var allocations = used.GetValueOrDefault(x.Id);
                var quantity = type == "SalesOrder" ? allocations.Quantity / x.BaseFactor : allocations.Quantity;
                return new { x.Id, x.ItemId, x.ItemCode, x.Description, x.UnitId, x.UnitCode, quantity = Money(x.Quantity),
                    usedQuantity = Money(quantity), remainingQuantity = Money(x.Quantity - quantity), total = Money(x.Total),
                    usedAmount = Money(allocations.Amount), remainingAmount = Money(type == "SalesInvoice" ? x.Total - allocations.Amount : x.Total),
                    unitPrice = Money(x.UnitPrice) };
            }) });
        }
        catch (MasterValidation) { return TenantAccess.Missing(); }
    }

    private static IResult Invalid(MasterValidation error) =>
        error.Field == "revision" ? ApiSupport.Problem(409, error.Message) : ApiSupport.Invalid(error.Field, error.Message);
}

