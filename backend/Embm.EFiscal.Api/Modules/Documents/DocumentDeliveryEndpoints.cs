using System.Globalization;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;
using static Embm.EFiscal.Api.Modules.MasterData.MasterRegistry;

namespace Embm.EFiscal.Api.Modules.Documents;

internal static partial class DocumentEndpoints
{
    private static void MapDelivery(RouteGroupBuilder group, bool development)
    {
        group.MapPost("/{type}/pdf-preview", PreviewPdf).RequireRateLimiting("company-email-preview");
        group.MapPost("/{type}/email-preview", PreviewEmail);
        group.MapPost("/template-preview/{type}", PreviewLayout).RequireRateLimiting("company-email-preview");
        group.MapGet("/{type}/{id:guid}/pdf", Download);
        group.MapGet("/delivery-history", History);
        if (!development) return;
        group.MapPost("/{type}/{id:guid}/resend", Resend).RequireRateLimiting("company-email-preview");
        group.MapPost("/{type}/{id:guid}/retry-render", RetryRendering).RequireRateLimiting("company-email-preview");
        group.MapPost("/{type}/{id:guid}/retry-delivery/{job:guid}", RetryDelivery).RequireRateLimiting("company-email-preview");
    }

    private static async Task<IResult> PreviewPdf(Guid subscriber, Guid company, string type, DocumentInput input, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, DocumentPdf renderer, CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "CreditMemo")) return TenantAccess.Missing();
        // End the read-only transaction before rendering to prevent a lengthy rendering operation holding a database snapshot.
        FiscalDocument? document = null;
        var validation = await ReadPreview(subscriber, company, type, input, http, db, async scope =>
        {
            document = (await assembly.Prepare(scope.Company, http.UserId(), type, input, service.LocalFixture(scope.Grant), ct)).Document;
            return Results.NoContent();
        }, ct);
        if (document is null) return validation;
        return PdfResult(renderer, document, null, type);
    }

    private static Task<IResult> PreviewEmail(Guid subscriber, Guid company, string type, DocumentInput input, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "CreditMemo")) return Task.FromResult(TenantAccess.Missing());
        return ReadPreview(subscriber, company, type, input, http, db, async scope =>
        {
            var doc = (await assembly.Prepare(scope.Company, http.UserId(), type, input, service.LocalFixture(scope.Grant), ct)).Document;
            var delivery = Read<DeliveryStamp>(doc.Snapshot.Delivery);
            var template = Read<DocumentTemplates>(doc.Snapshot.Templates).Email!;
            var message = DevelopmentDocumentMailbox.RenderEmail(doc, template.Content, delivery.Recipient, true);
            return Results.Ok(new { message.Subject, message.Body, message.Brand, message.Signature, message.Footer, delivery.Recipient, preview = true });
        }, ct);
    }

    private static async Task<IResult> PreviewLayout(Guid subscriber, Guid company, string type, PdfLayout layout, HttpContext http,
        EfiscalDbContext db, DocumentPdf renderer, CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "CreditMemo") || await Access(subscriber, company, Permissions.CompanyManage, http, db, ct) is null)
            return TenantAccess.Missing();
        if (!TemplateRules.ValidLayout(layout)) return ApiSupport.Invalid("layout", "Review the document layout and required fields.");
        var seller = new PartySnapshot("SAMPLE", "Harbor Supply · Sample", null, "000000000", null, "Fictional company address", null, null, null,
            "office@example.test", "VAT");
        var buyer = new PartySnapshot("SAMPLE", "Sample customer", null, null, null, "Fictional buyer address", null, null, null, null, null);
        var sample = new FiscalDocument { Type = type, Date = new(2026, 1, 15), DueDate = new(2026, 1, 30), ExchangeRate = 1,
            Total = 112m, AmountDue = 112m, VatableSales = 100m, Vat = 12m, GrossSales = 100m,
            Lines = [new FiscalDocumentLine { Position = 1, ItemCode = "SAMPLE", Description = "Sample service", Quantity = 1,
                UnitCode = "EA", UnitPrice = 100m, Vat = 12m, Total = 112m }],
            Snapshot = new FiscalDocumentSnapshot {
                Parties = Freeze(new DocumentParties(seller, buyer, "HQ", "Sample branch", "Fictional location")),
                Header = Freeze(new DocumentHeader(null, type == "CreditMemo" ? "Sample adjustment" : null, null, null, null, null, null, null,
                    null, type == "CreditMemo" ? "SI-SAMPLE" : null, null, null, new { }))
            }
        };
        return PdfResult(renderer, sample, layout, type);
    }

    private static IResult PdfResult(DocumentPdf renderer, FiscalDocument document, PdfLayout? layout, string type)
    {
        try
        {
            return Results.File(layout is null ? renderer.Render(document, true) : renderer.Render(document, layout, true),
                "application/pdf", $"{type}-preview.pdf");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or InvalidDataException or FormatException
            or NotSupportedException or IOException)
        { return ApiSupport.Invalid("layout", "The selected layout could not be rendered. Check the PNG and selected settings."); }
    }

    private static async Task<IResult> Download(Guid subscriber, Guid company, string type, Guid id, bool? inline, HttpContext http, EfiscalDbContext db,
        CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "CreditMemo") || await Access(subscriber, company, Permission(type, false), http, db, ct, existingRecords: true) is null)
            return TenantAccess.Missing();
        var result = await (from document in db.FiscalDocuments.AsNoTracking()
            join issued in db.IssuedDocumentFiles.AsNoTracking()
                on new { document.SubscriberId, document.CompanyId, document.Id }
                equals new { issued.SubscriberId, issued.CompanyId, Id = issued.DocumentId }
            where document.SubscriberId == subscriber && document.CompanyId == company && document.Id == id && document.Type == type
            select new { document.Number, issued.Content, issued.Sha256 }).SingleOrDefaultAsync(ct);
        if (result is null) return TenantAccess.Missing();
        http.Response.Headers["X-Document-SHA256"] = result.Sha256;
        if (inline == true) http.Response.Headers.ContentDisposition = "inline";
        return Results.File(result.Content, "application/pdf", inline == true ? null : result.Number.Replace('/', '-') + ".pdf");
    }

    private static async Task<IResult> History(Guid subscriber, Guid company, int? page, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await Access(subscriber, company, "Invoice.View", http, db, ct) is not { } scope) return TenantAccess.Missing();
        var kinds = Types.Where(kind => kind != "SalesOrder" && scope.Grant.Grants.Contains(Permission(kind, false))).ToArray();
        var query = from row in Attempts(db, subscriber, company)
            join doc in db.FiscalDocuments.AsNoTracking() on new { row.SubscriberId, row.CompanyId, Id = row.DocumentId }
                equals new { doc.SubscriberId, doc.CompanyId, doc.Id }
            where kinds.Contains(doc.Type)
            select new { document = new { doc.Id, doc.Type, doc.Number }, attempt = row };
        var index = Math.Clamp(page ?? 1, 1, 100_000);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.attempt.At).ThenByDescending(x => x.attempt.Id)
            .Skip((index - 1) * 25).Take(25).ToArrayAsync(ct);
        return Results.Ok(new { page = index, pageSize = 25, total, items });
    }

    private static Task<IResult> Resend(Guid subscriber, Guid company, string type, Guid id, ResendInput input, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct) =>
        Enqueue(subscriber, company, type, id, input, null, "Email", http, db, assembly, service, ct);

    private static Task<IResult> RetryRendering(Guid subscriber, Guid company, string type, Guid id, ResendInput input, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct) =>
        Enqueue(subscriber, company, type, id, input, null, "Render", http, db, assembly, service, ct);

    private static Task<IResult> RetryDelivery(Guid subscriber, Guid company, string type, Guid id, Guid job, HttpContext http,
        EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct) =>
        Enqueue(subscriber, company, type, id, new(null, null), job, "Email", http, db, assembly, service, ct);

    private static async Task<IResult> Enqueue(Guid subscriber, Guid company, string type, Guid id, ResendInput input, Guid? retryJob,
        string kind, HttpContext http, EfiscalDbContext db, DocumentAssembly assembly, DocumentCommitService service, CancellationToken ct)
    {
        if (type is not ("SalesInvoice" or "CreditMemo") || await Access(subscriber, company, ResendPermission, http, db, ct) is null)
            return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await Access(subscriber, company, ResendPermission, http, db, ct) is not { } scope ||
            !service.LocalFixture(scope.Grant) || !scope.Grant.Grants.Contains(Permission(type, false))) return TenantAccess.Missing();
        var doc = await db.FiscalDocuments.AsNoTracking().Include(x => x.Snapshot)
            .SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id && x.Type == type && x.DevelopmentFixture, ct);
        if (doc is null) return TenantAccess.Missing();
        var messages = db.DocumentOutboxMessages.Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == id && x.Kind == kind);
        var state = await db.DocumentProcessing.SingleAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.DocumentId == id, ct);
        var now = DateTimeOffset.UtcNow;
        try
        {
            DocumentOutboxMessage? job;
            if (retryJob.HasValue)
            {
                job = await messages.SingleOrDefaultAsync(x => x.Id == retryJob, ct);
                if (job is null) return TenantAccess.Missing();
                if (job.State is "Pending" or "Processing") return Results.Accepted(value: new { job.Id, job.State });
                if (job.State is not ("Failed" or "RequiresReview" or "RetryScheduled")) return ApiSupport.Problem(409, "This delivery does not require retry.");
                job.State = "Pending";
                job.AvailableAt = now;
            }
            else
            {
                var key = Key(input.Key);
                var operation = "manual-" + Digest(new { key, actor = http.UserId() });
                job = await messages.SingleOrDefaultAsync(x => x.Operation == operation, ct);
                if (job is not null)
                {
                    Need(string.IsNullOrWhiteSpace(input.Recipient) || job.Recipient?.Equals(input.Recipient.Trim(), StringComparison.OrdinalIgnoreCase) == true,
                        "recipient", "This operation key already identifies another recipient.");
                    return Results.Accepted(value: new { job.Id, job.State });
                }
                if (await messages.AnyAsync(x => x.State == "Pending" || x.State == "Processing" || x.State == "RetryScheduled", ct))
                    return ApiSupport.Problem(409, "This document already has pending work of this type.");
                string? recipient = null;
                if (kind == "Email")
                {
                    Need(state.Rendering == "Ready", "pdf", "Wait until the original PDF is available.");
                    var pinned = Read<DeliveryStamp>(doc.Snapshot.Delivery);
                    recipient = string.IsNullOrWhiteSpace(input.Recipient) ? pinned.Recipient : input.Recipient.Trim();
                    var current = await assembly.Recipients(subscriber, company, doc.CustomerId, ct);
                    Need(recipient is not null && DocumentCommitService.TestAddress(recipient) &&
                        (string.Equals(recipient, pinned.Recipient, StringComparison.OrdinalIgnoreCase) || current.Contains(recipient, StringComparer.OrdinalIgnoreCase)),
                        "recipient", "Choose the original address or a current authorized .test address for this customer.");
                }
                else Need(state.Rendering is "Failed" or "RequiresReview", "pdf", "The original PDF already exists or is queued.");
                job = DocumentCommitService.Job(doc, kind, operation, now, recipient);
                db.DocumentOutboxMessages.Add(job);
            }
            if (kind == "Email") state.ElectronicDelivery = "Queued";
            else { state.Rendering = "Queued"; state.ElectronicDelivery = "PendingPdf"; }
            db.Audit("Documents." + (kind == "Render" ? "PdfRetry" : retryJob.HasValue ? "DeliveryRetry" : "ResendQueued"),
                http.UserId(), subscriber, id);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return Results.Accepted(value: new { job.Id, job.State });
        }
        catch (MasterValidation error) { return Invalid(error); }
    }
}

