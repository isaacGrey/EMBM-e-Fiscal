using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class CanonicalInvoices
{
    internal const string Version = "embm-invoice-v1";
    internal const string Notice = "Internal structured electronic invoice — official BIR transmission format awaiting verified specifications.";
    internal static bool Supports(string type) => type is "SalesInvoice" or "CreditMemo";

    internal static async Task Capture(EfiscalDbContext db, FiscalDocument document, CancellationToken ct)
    {
        var candidates = await RegulatoryEligibility.Candidates(db, document.SubscriberId, document.CompanyId,
            document.BranchId, document.Type, ct);
        var date = RegulatoryEligibility.ManilaDate(document.CommittedAt);
        db.ReportingPreparations.Add(Build(document, document.ActorId, document.CommittedAt,
            candidates.Where(x => x.IsCurrent(date) && x.IsCurrent(document.Date)).ToArray()));
    }

    internal static ReportingPreparation Build(FiscalDocument doc, Guid actor, DateTimeOffset generatedAt,
        RegulatoryEligibility.Candidate[]? authorizations)
    {
        // PostgreSQL timestamps have microsecond precision. Normalize before hashing at commitment.
        var committedAt = doc.CommittedAt.ToUniversalTime();
        committedAt = committedAt.AddTicks(-(committedAt.Ticks % 10));
        var parties = Read<DocumentParties>(doc.Snapshot.Parties);
        var header = Read<DocumentHeader>(doc.Snapshot.Header);
        var fiscalSnapshot = new
        {
            document = new
            {
                doc.Id, doc.Number, doc.Type, doc.Date, doc.DueDate, committedAt, doc.DevelopmentFixture, doc.Currency, doc.Precision,
                exchangeRate = Money(doc.ExchangeRate), doc.SeriesId, doc.Snapshot.Numbering, header.PaymentTerm,
                originalInvoiceId = doc.Type == "CreditMemo" ? doc.SourceDocumentId : null,
                sourceOrderId = doc.Type == "SalesInvoice" ? doc.SourceDocumentId : null,
                header.SourceNumber, doc.ExternalOrderNumber, header.AdjustmentKind, header.AdjustmentReason
            },
            seller = new
            {
                doc.CompanyId, doc.BranchId, parties.BranchCode, parties.BranchName, parties.BranchAddress,
                parties.Seller.Name, parties.Seller.TradeName, parties.Seller.Tin, parties.Seller.Rdo,
                parties.Seller.RegisteredAddress, parties.Seller.VatStatus
            },
            buyer = new
            {
                doc.CustomerId, parties.Customer.Code, parties.Customer.Name, parties.Customer.TradeName, parties.Customer.Tin,
                parties.Customer.RegisteredAddress, parties.Customer.BillingAddress, parties.Customer.Classification
            },
            lines = doc.Lines.OrderBy(x => x.Position).Select(line => new
            {
                line.Id, line.Position, line.ItemId, line.ItemCode, line.Description, line.UnitId, line.UnitCode,
                quantity = Money(line.Quantity), unitPrice = Money(line.UnitPrice), gross = Money(line.Gross),
                commercialDiscount = Money(line.CommercialDiscount), statutoryDiscount = Money(line.StatutoryDiscount),
                vatableSales = Money(line.VatableSales), vatExemptSales = Money(line.ExemptSales),
                zeroRatedSales = Money(line.ZeroRatedSales), nonVatSales = Money(line.NonVatSales),
                vat = Money(line.Vat), incomeWithholding = Money(line.IncomeWithholding), vatWithholding = Money(line.VatWithholding),
                total = Money(line.Total), taxClassification = TaxClassification(line.Terms), financialTerms = line.Terms
            }).ToArray(),
            totals = DocumentDtos.Totals(doc)
        };
        var limitations = new List<string>();
        if (authorizations is null) limitations.Add("Authorization references were not captured at commitment.");
        else if (authorizations.Length == 0) limitations.Add("No current documented authorization references were found at commitment.");
        if (string.IsNullOrWhiteSpace(parties.Seller.Rdo)) limitations.Add("Seller RDO was not captured at commitment.");
        if (string.IsNullOrWhiteSpace(parties.Customer.Classification)) limitations.Add("Buyer classification was not captured at commitment.");
        var bytes = CanonicalBytes(new
        {
            schemaVersion = Version, format = "InternalCanonical",
            originalFinancialSnapshotSha256 = Digest(fiscalSnapshot),
            fiscalSnapshot, documentedAuthorizationReferences = authorizations?.Select(x => new { x.Id, x.Kind, x.Number, x.Reference }),
            evidenceLimitations = limitations
        });
        return new ReportingPreparation
        {
            SubscriberId = doc.SubscriberId, CompanyId = doc.CompanyId, DocumentId = doc.Id, ActorId = actor,
            SchemaVersion = Version, CreatedAt = generatedAt, Content = Encoding.UTF8.GetString(bytes),
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
        };
    }

    private static string? TaxClassification(JsonElement terms)
    {
        while (terms.ValueKind == JsonValueKind.Object)
        {
            if (terms.TryGetProperty("classification", out var classification) && classification.ValueKind == JsonValueKind.String)
                return classification.GetString();
            if (!terms.TryGetProperty("originalTerms", out terms)) break;
        }
        return null;
    }

    internal static void MapCanonicalInvoices(RouteGroupBuilder group)
    {
        group.MapGet("/{type}/{id:guid}/structured", View);
        group.MapGet("/{type}/{id:guid}/structured/download", Download);
        group.MapPost("/{type}/{id:guid}/structured/generate", Generate);
    }

    private static IQueryable<ReportingPreparation> Envelopes(EfiscalDbContext db, Guid subscriber, Guid company, Guid id) =>
        db.ReportingPreparations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.DocumentId == id && x.SchemaVersion == Version);

    private static async Task<IResult> View(Guid subscriber, Guid company, string type, Guid id, HttpContext http,
        EfiscalDbContext db, CancellationToken ct) =>
        await Resolve(subscriber, company, type, id, http, db, ct, download: false);

    private static async Task<IResult> Download(Guid subscriber, Guid company, string type, Guid id, HttpContext http,
        EfiscalDbContext db, CancellationToken ct) =>
        await Resolve(subscriber, company, type, id, http, db, ct, download: true);

    private static async Task<IResult> Resolve(Guid subscriber, Guid company, string type, Guid id, HttpContext http,
        EfiscalDbContext db, CancellationToken ct, bool download)
    {
        if (!Supports(type) || await DocumentEndpoints.Access(subscriber, company, Permission(type, false), http, db, ct) is null)
            return TenantAccess.Missing();
        var document = await db.FiscalDocuments.AsNoTracking().Where(d => d.SubscriberId == subscriber && d.CompanyId == company &&
            d.Id == id && d.Type == type).Select(d => new { d.Number }).SingleOrDefaultAsync(ct);
        if (document is null) return TenantAccess.Missing();
        var row = await Envelopes(db, subscriber, company, id).SingleOrDefaultAsync(ct);
        if (row is null) return download ? TenantAccess.Missing() : Results.Ok(new { available = false, notice = Notice });

        var bytes = Encoding.UTF8.GetBytes(row.Content);
        if (!StructuredInvoiceValidation.HasDigest(bytes, row.Sha256))
            return ApiSupport.Problem(409, "Structured invoice integrity check failed.");
        using var parsed = JsonDocument.Parse(bytes);
        var documentHash = parsed.RootElement.GetProperty("originalFinancialSnapshotSha256").GetString();
        if (Digest(parsed.RootElement.GetProperty("fiscalSnapshot")) != documentHash)
            return ApiSupport.Problem(409, "Committed fiscal snapshot integrity check failed.");
        if (download)
        {
            http.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(bytes, "application/json", document.Number + ".json");
        }
        var schema = await db.Set<RegulatorySchemaVersion>().AsNoTracking().SingleOrDefaultAsync(x => x.Version == row.SchemaVersion, ct);
        var schemaCompatible = StructuredInvoiceValidation.SchemaCompatible(schema, type);
        var issues = StructuredInvoiceValidation.Issues(parsed.RootElement, id, type, document.Number);
        if (!schemaCompatible) issues = [.. issues, "The matching internal schema is missing or failed its checksum."];
        var limitations = parsed.RootElement.GetProperty("evidenceLimitations").GetArrayLength();
        return Results.Ok(new
        {
            available = true, notice = Notice, schemaVersion = row.SchemaVersion, generatedAt = row.CreatedAt,
            row.Sha256, integrityVerified = true, officialValidation = "NotPerformed", data = parsed.RootElement.Clone(),
            validation = new
            {
                internalSchema = issues.Length == 0 ? "Valid" : "Invalid", issues, schemaChecksum = schemaCompatible ? schema!.Sha256 : null,
                regulatoryEvidence = limitations == 0 ? "Captured" : "Incomplete", officialSchema = "NotAvailable"
            }
        });
    }

    private static async Task<IResult> Generate(Guid subscriber, Guid company, string type, Guid id, HttpContext http,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (!Supports(type) || await DocumentEndpoints.Access(subscriber, company, Permission(type, false), http, db, ct) is null)
            return TenantAccess.Missing();
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        if (await DocumentEndpoints.Access(subscriber, company, Permission(type, false), http, db, ct) is null)
            return TenantAccess.Missing();
        var doc = await db.FiscalDocuments.AsNoTracking().Include(d => d.Snapshot).Include(d => d.Lines)
            .SingleOrDefaultAsync(d => d.SubscriberId == subscriber && d.CompanyId == company && d.Id == id && d.Type == type, ct);
        if (doc is null) return TenantAccess.Missing();
        if (!await Envelopes(db, subscriber, company, id).AnyAsync(ct))
        {
            db.ReportingPreparations.Add(Build(doc, http.UserId(), DateTimeOffset.UtcNow, authorizations: null));
            db.Audit("Documents.LegacyStructuredInvoiceGenerated", http.UserId(), subscriber, id);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return await View(subscriber, company, type, id, http, db, ct);
    }
}
