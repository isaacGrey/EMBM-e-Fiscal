using Embm.EFiscal.Api.Modules.Connect;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal sealed record HistoricalAdjustment(string Id, string Line, decimal Quantity, decimal Gross, decimal Discount,
    decimal Taxable, decimal Vat, decimal Total);

internal sealed record HistoricalCreditOriginal(Guid ReferenceId, Guid ConnectionId, string Account, string ExternalCompany,
    string RecordId, string Number, DateOnly Date, Guid? BranchId, string Hash, Guid CustomerId, PartySnapshot Customer,
    string LineKey, FiscalDocumentLine Line, HistoricalAdjustment[] Adjustments)
{
    public string AllocationKey => "historical-credit:" + Digest(new { Account, ExternalCompany, RecordId });
    public object Evidence => new { ReferenceId, ConnectionId, Account, ExternalCompany, RecordId, Number, Date, Hash,
        Currency = "PHP", ExchangeRate = 1m, Customer, LineKey,
        Line = new { Line.ItemId, Line.UnitId, Line.Quantity, Line.UnitPrice, Line.Gross, Line.CommercialDiscount, Line.VatableSales, Line.Vat, Line.Total, Line.Terms },
        Adjustments, Consistency = "Immutable development fixture ledger" };
}

internal sealed class HistoricalCreditSource(EfiscalDbContext db, IEnumerable<DevelopmentAccountingRead> readers)
{
    public async Task<HistoricalCreditOriginal?> Read(Guid subscriber, Guid company, Guid referenceId, CancellationToken ct)
    {
        if (readers.SingleOrDefault() is not { } accounting) return null;
        var snapshot = await (from reference in db.AccountingReferences.AsNoTracking()
            join observation in db.AccountingObservations.AsNoTracking() on
                new { reference.SubscriberId, reference.CompanyId, Id = reference.ObservationId } equals
                new { observation.SubscriberId, observation.CompanyId, observation.Id }
            where reference.SubscriberId == subscriber && reference.CompanyId == company && reference.Id == referenceId
                && observation.Type == "SalesInvoice"
            select observation).SingleOrDefaultAsync(ct);
        if (snapshot is null) return null;
        var link = await db.ExternalConnections.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.Id == snapshot.ConnectionId && x.ConnectorVersionId == snapshot.ConnectorVersionId &&
            x.ExternalAccountId == snapshot.Account && x.ExternalResourceId == snapshot.ExternalCompany, ct);
        if (link is null || link.MappingVersion is null) return null;
        var read = await accounting.Read(link, null, snapshot.ExternalId, "SalesInvoice", ct);
        if (read.Status != "Complete" || read.Records is not [var invoice] || invoice.Date is not DateOnly date ||
            !DevelopmentAccountingRead.HasCompleteCreditHistory(invoice) || invoice.Values.Lines is not [var line] ||
            Digest(Freeze(invoice.Values)) != snapshot.Hash) return null;

        var mapping = await db.ConnectionMappings.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber &&
            x.CompanyId == company && x.ConnectionId == link.Id && x.Number == link.MappingVersion, ct);
        if (mapping is null) return null;
        var master = db.MasterRecords.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Active);
        var item = await master.SingleOrDefaultAsync(x => x.Id == mapping.ItemId && x.Kind == "items", ct);
        var unit = await master.SingleOrDefaultAsync(x => x.Id == mapping.UnitId && x.Kind == "units", ct);
        var customer = await master.SingleOrDefaultAsync(x => x.Id == mapping.CustomerId && x.Kind == "customers", ct);
        if (item is null || unit is null || customer is null) return null;
        var amounts = line.Values;
        var adjustments = invoice.Values.Header["priorAdjustments"].Deserialize<HistoricalAdjustment[]>(Json);
        if (adjustments is not [var prior] || prior.Line != line.Reference) return null;
        var original = new FiscalDocumentLine
        {
            ItemId = item.Id, ItemCode = item.Code, UnitId = unit.Id, UnitCode = unit.Code, BaseFactor = 1m,
            Description = "Original development accounting service", Quantity = amounts["quantity"].GetDecimal(),
            UnitPrice = amounts["unitPrice"].GetDecimal(), Gross = amounts["gross"].GetDecimal(),
            CommercialDiscount = amounts["commercialDiscount"].GetDecimal(), VatableSales = amounts["vatableSales"].GetDecimal(),
            Vat = amounts["vat"].GetDecimal(), Total = amounts["total"].GetDecimal(),
            Terms = Freeze(new { classification = "VATable", inclusive = false, taxCode = "VAT12" })
        };
        // Keep the original external buyer evidence. Delivery uses the separately authorized local recipient list.
        var party = new PartySnapshot("DEMO-BUYER", "Development Buyer", null, null, null, null, null, null, null, null, null);
        return new(referenceId, link.Id, link.ExternalAccountId, link.ExternalResourceId, invoice.Id, invoice.Number,
            date, link.BranchId, snapshot.Hash, customer.Id, party, line.Reference, original, adjustments);
    }
}

internal sealed record HistoricalCreditLink(Guid ConnectionId, string Account, string ExternalCompany, string RecordId)
{
    public static HistoricalCreditLink? Of(FiscalDocument document)
    {
        if (document.Type != "CreditMemo" || Read<DocumentHeader>(document.Snapshot.Header).ExternalSource is not JsonElement evidence ||
            !evidence.TryGetProperty("consistency", out _)) return null;
        return evidence.Deserialize<HistoricalCreditLink>(SpecImport.Json);
    }

    public bool Matches(ExternalConnection link) => link.Id == ConnectionId && link.ExternalAccountId == Account &&
        link.ExternalResourceId == ExternalCompany;
}
