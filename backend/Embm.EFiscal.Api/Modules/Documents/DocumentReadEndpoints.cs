using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal static partial class DocumentEndpoints
{
    private static async Task<IResult> Register(Guid subscriber, Guid company, string type, string? q, string? number,
        Guid? customer, Guid? branch, DateOnly? from, DateOnly? through, string? source, bool? charged, string? delivery,
        string? accounting, string? regulator, int? page, bool? export, HttpContext http, EfiscalDbContext db,
        DocumentRelationships documents, CancellationToken ct)
    {
        if (!Types.Contains(type) || await Access(subscriber, company, Permission(type, false), http, db, ct, existingRecords: true) is not { } scope
            || export == true && !scope.Grant.Grants.Contains(ExportPermission)) return TenantAccess.Missing();
        if (new[] { q, number, source, delivery, accounting, regulator }.Any(x => !ApiSupport.Optional(x, 120)) || from > through)
            return ApiSupport.Invalid("filter", "Choose supported filter values and a valid date range.");
        var query = documents.Scope(subscriber, company).AsNoTracking().Where(x => x.Type == type);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = Pattern(q);
            query = query.Where(x => EF.Functions.ILike(x.Number, pattern, @"\") || EF.Functions.ILike(x.CustomerCode, pattern, @"\")
                || EF.Functions.ILike(x.CustomerName, pattern, @"\"));
        }
        if (!string.IsNullOrWhiteSpace(number)) query = query.Where(x => x.Number == number.Trim());
        if (customer.HasValue) query = query.Where(x => x.CustomerId == customer);
        if (branch.HasValue) query = query.Where(x => x.BranchId == branch);
        if (from.HasValue) query = query.Where(x => x.Date >= from);
        if (through.HasValue) query = query.Where(x => x.Date <= through);
        if (!string.IsNullOrWhiteSpace(source))
        {
            var reference = Pattern(source);
            query = query.Where(x => x.SourceDocument != null && EF.Functions.ILike(x.SourceDocument.Number, reference, @"\")
                || x.ExternalOrderNumber != null && EF.Functions.ILike(x.ExternalOrderNumber, reference, @"\"));
        }
        if (charged.HasValue) query = query.Where(x => (x.Credits != 0) == charged);
        if (!string.IsNullOrWhiteSpace(delivery)) query = query.Where(x => x.Processing.ElectronicDelivery == delivery);
        if (!string.IsNullOrWhiteSpace(accounting)) query = query.Where(x => x.Processing.Accounting == accounting);
        if (!string.IsNullOrWhiteSpace(regulator)) query = query.Where(x => x.Processing.Regulator == regulator);
        var index = Math.Clamp(page ?? 1, 1, 100_000);
        var total = await query.CountAsync(ct);
        var sorted = query.Include(x => x.Processing).Include(x => x.SourceDocument)
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.CommittedAt).ThenBy(x => x.Id);
        if (export == true && total > 1000) return ApiSupport.Invalid("export", "Filter to 1,000 documents or fewer.");
        var rows = await (export == true ? sorted.Take(1000) : sorted.Skip((index - 1) * 25).Take(25)).ToArrayAsync(ct);
        if (export != true) return Results.Ok(new { page = index, pageSize = 25, total,
            items = rows.Select(x => new { document = DocumentDtos.Receipt(x), sourceNumber = x.SourceDocument?.Number ?? x.ExternalOrderNumber,
                rendering = x.Processing.Rendering, electronicDelivery = x.Processing.ElectronicDelivery, accounting = x.Processing.Accounting,
                regulator = x.Processing.Regulator }) });
        List<string[]> output = [["Number", "Date", "Customer", "Currency", "Total", "Credits", "Source", "Electronic delivery", "Accounting", "Regulator"]];
        output.AddRange(rows.Select(x => new[] { x.Number, x.Date.ToString("yyyy-MM-dd"), x.CustomerName, x.Currency, Money(x.Total), x.Credits.ToString(),
            x.SourceDocument?.Number ?? x.ExternalOrderNumber ?? "", x.Processing.ElectronicDelivery, x.Processing.Accounting, x.Processing.Regulator }));
        return Results.File(MasterSpreadsheet.Write(output, "csv"), "text/csv; charset=utf-8", type + "-register.csv");
    }

    private static async Task<IResult> Detail(Guid subscriber, Guid company, string type, Guid id, HttpContext http, EfiscalDbContext db,
        DocumentRelationships documents, CancellationToken ct)
    {
        if (!Types.Contains(type) || await Access(subscriber, company, Permission(type, false), http, db, ct, existingRecords: true) is not { } scope)
            return TenantAccess.Missing();
        var doc = await documents.Scope(subscriber, company).AsNoTracking().Include(x => x.Snapshot).Include(x => x.Processing)
            .Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.Type == type, ct);
        if (doc is null) return TenantAccess.Missing();
        var visibleTypes = Types.Where(kind => scope.Grant.Grants.Contains(Permission(kind, false))).ToArray();
        var related = await documents.Scope(subscriber, company).AsNoTracking().Where(x => visibleTypes.Contains(x.Type)
            && (x.Id == doc.SourceDocumentId || x.SourceDocumentId == doc.Id)).OrderByDescending(x => x.Date).Take(100).ToArrayAsync(ct);
        var file = await db.IssuedDocumentFiles.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == id)
            .Select(x => new { x.Sha256, x.RenderedAt }).SingleOrDefaultAsync(ct);
        var delivery = await Attempts(db, subscriber, company, id).Take(100).ToArrayAsync(ct);
        var audit = await db.SecurityAudits.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.TargetId == id)
            .OrderByDescending(x => x.At).Select(x => new { x.ActorId, x.Action, x.At }).Take(100).ToArrayAsync(ct);
        var template = Read<DocumentTemplates>(doc.Snapshot.Templates);
        return Results.Ok(new { document = DocumentDtos.Receipt(doc), totals = DocumentDtos.Totals(doc), doc.DueDate, doc.Precision,
            exchangeRate = Money(doc.ExchangeRate), doc.Snapshot.Parties, doc.Snapshot.Header, doc.Snapshot.Numbering,
            templates = new { pdf = template.Pdf is null ? null : new { template.Pdf.Id, template.Pdf.Name, template.Pdf.Version },
                email = template.Email is null ? null : new { template.Email.Id, template.Email.Name, template.Email.Version } },
            lines = doc.Lines.OrderBy(x => x.Position).Select(DocumentDtos.Line), doc.Processing.Rendering, doc.Processing.ElectronicDelivery,
            doc.Processing.Accounting, doc.Processing.Regulator, file, related = related.Select(DocumentDtos.Receipt), delivery, audit });
    }

    private static IQueryable<DocumentDeliveryAttempt> Attempts(EfiscalDbContext db, Guid subscriber, Guid company, Guid? document = null) =>
        db.DocumentDeliveryAttempts.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company
            && (document == null || x.DocumentId == document)).OrderByDescending(x => x.At).ThenByDescending(x => x.Id);

    private static string Pattern(string value) => "%" + value.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
}

