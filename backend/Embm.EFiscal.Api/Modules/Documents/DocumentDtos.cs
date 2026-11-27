using static Embm.EFiscal.Api.Modules.Documents.DocumentRules;

namespace Embm.EFiscal.Api.Modules.Documents;

internal static class DocumentDtos
{
    public static object Receipt(FiscalDocument doc) => new
    {
        doc.Id, doc.Type, doc.Number, doc.Date, doc.Currency, doc.CustomerId, doc.CustomerCode, doc.CustomerName,
        doc.BranchId, doc.Credits, doc.CommittedAt, doc.DevelopmentFixture, doc.SourceDocumentId, doc.ExternalOrderNumber,
        total = Money(doc.Total), amountDue = Money(doc.AmountDue)
    };

    public static object Totals(FiscalDocument doc) => new
    {
        grossSales = Money(doc.GrossSales), commercialDiscount = Money(doc.CommercialDiscount), statutoryDiscount = Money(doc.StatutoryDiscount),
        vatableSales = Money(doc.VatableSales), vatExemptSales = Money(doc.ExemptSales), zeroRatedSales = Money(doc.ZeroRatedSales),
        nonVatSales = Money(doc.NonVatSales), vat = Money(doc.Vat), incomeWithholding = Money(doc.IncomeWithholding),
        vatWithholding = Money(doc.VatWithholding), total = Money(doc.Total), amountDue = Money(doc.AmountDue), rounding = Money(doc.Rounding)
    };

    public static object Line(FiscalDocumentLine line) => new
    {
        line.Id, line.Position, line.ItemId, line.ItemCode, line.Description, line.UnitId, line.UnitCode,
        quantity = Money(line.Quantity), unitPrice = Money(line.UnitPrice), baseFactor = Money(line.BaseFactor),
        gross = Money(line.Gross), vat = Money(line.Vat), commercialDiscount = Money(line.CommercialDiscount),
        statutoryDiscount = Money(line.StatutoryDiscount), total = Money(line.Total), line.Terms
    };

    public static object Preview(PreparedDocument prepared, long? balance)
    {
        var doc = prepared.Document;
        var template = Read<DocumentTemplates>(doc.Snapshot.Templates);
        var delivery = Read<DeliveryStamp>(doc.Snapshot.Delivery);
        return new
        {
            prepared.Confirmation, doc.Type, doc.Date, doc.DueDate, doc.Currency, doc.Precision, exchangeRate = Money(doc.ExchangeRate),
            doc.CustomerId, doc.CustomerCode, doc.CustomerName, doc.SourceDocumentId, doc.ExternalOrderNumber,
            documentNumber = (string?)null, seriesConfigured = prepared.Series is not null, totals = Totals(doc),
            parties = doc.Snapshot.Parties, lines = doc.Lines.Select(Line), recipients = prepared.Recipients, delivery.Recipient,
            senderConfigured = delivery.Sender?.Provider == "platform",
            pdfTemplate = template.Pdf is null ? null : new { template.Pdf.Id, template.Pdf.Name, template.Pdf.Version },
            emailTemplate = template.Email is null ? null : new { template.Email.Id, template.Email.Name, template.Email.Version },
            availableCredits = balance, doc.Credits, remainingCredits = balance is null ? (long?)null : Math.Max(0, balance.Value - doc.Credits)
        };
    }
}

