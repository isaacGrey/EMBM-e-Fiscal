using System.Text.Json;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Documents;

public sealed class FiscalDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid SeriesId { get; set; }
    public Guid ActorId { get; set; }
    public string Type { get; set; } = "";
    public string Number { get; set; } = "";
    public string CustomerCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public DateOnly Date { get; set; }
    public DateOnly DueDate { get; set; }
    public DateTimeOffset CommittedAt { get; set; }
    public int LineCount { get; set; }
    public int Credits { get; set; }
    public string Currency { get; set; } = "PHP";
    public int Precision { get; set; } = 2;
    public decimal ExchangeRate { get; set; }
    public decimal GrossSales { get; set; }
    public decimal CommercialDiscount { get; set; }
    public decimal StatutoryDiscount { get; set; }
    public decimal VatableSales { get; set; }
    public decimal ExemptSales { get; set; }
    public decimal ZeroRatedSales { get; set; }
    public decimal NonVatSales { get; set; }
    public decimal Vat { get; set; }
    public decimal IncomeWithholding { get; set; }
    public decimal VatWithholding { get; set; }
    public decimal Total { get; set; }
    public decimal AmountDue { get; set; }
    public decimal Rounding { get; set; }
    public bool DevelopmentFixture { get; set; }
    public bool PartialFulfillment { get; set; }
    public Guid? HistoricalInvoiceReferenceId { get; set; }
    public string? ExternalOrderKey { get; set; }
    public string? ExternalOrderNumber { get; set; }
    public Guid? SourceDocumentId { get; set; }
    public FiscalDocument? SourceDocument { get; set; }
    public List<FiscalDocumentLine> Lines { get; set; } = [];
    public FiscalDocumentSnapshot Snapshot { get; set; } = null!;
    public DocumentProcessing Processing { get; set; } = null!;
}

public sealed class FiscalDocumentLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public int Position { get; set; }
    public string ItemCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnitCode { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal BaseFactor { get; set; }
    public decimal Quantity { get; set; }
    public decimal Gross { get; set; }
    public decimal CommercialDiscount { get; set; }
    public decimal StatutoryDiscount { get; set; }
    public decimal VatableSales { get; set; }
    public decimal ExemptSales { get; set; }
    public decimal ZeroRatedSales { get; set; }
    public decimal NonVatSales { get; set; }
    public decimal Vat { get; set; }
    public decimal IncomeWithholding { get; set; }
    public decimal VatWithholding { get; set; }
    public decimal Total { get; set; }
    public JsonElement Terms { get; set; }
}

public sealed class FiscalDocumentSnapshot
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public JsonElement Parties { get; set; }
    public JsonElement Header { get; set; }
    public JsonElement Numbering { get; set; }
    public JsonElement Templates { get; set; }
    public JsonElement Delivery { get; set; }
}

public sealed class DocumentNumberSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string Type { get; set; } = "";
    public int Revision { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public string Prefix { get; set; } = "";
    public int Digits { get; set; }
    public long NextNumber { get; set; }
    public long InitialNumber { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentCommitment
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string Key { get; set; } = "";
    public string Digest { get; set; } = "";
    public Guid DocumentId { get; set; }
}

public sealed class DocumentAllocation
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid LineId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? ExternalOrderKey { get; set; }
    public string? ExternalLineKey { get; set; }
    public string Kind { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal Amount { get; set; }
}

public sealed class DocumentProcessing
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string Rendering { get; set; } = "NotApplicable";
    public string ElectronicDelivery { get; set; } = "NotApplicable";
    public string Accounting { get; set; } = "NotConfigured";
    public string Regulator { get; set; } = "NotApplicable";
}

public sealed class DocumentOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string Kind { get; set; } = "";
    public string State { get; set; } = "Pending";
    public string Operation { get; set; } = "";
    public string? Recipient { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset AvailableAt { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public Guid? LeaseToken { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class IssuedDocumentFile
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public byte[] Content { get; set; } = [];
    public string Sha256 { get; set; } = "";
    public DateTimeOffset RenderedAt { get; set; }
}

public sealed class DocumentDeliveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid JobId { get; set; }
    public int Attempt { get; set; }
    public string Status { get; set; } = "";
    public string Recipient { get; set; } = "";
    public string? ProviderReference { get; set; }
    public string? AttachmentSha256 { get; set; }
    public string? ErrorCategory { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset At { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
}

