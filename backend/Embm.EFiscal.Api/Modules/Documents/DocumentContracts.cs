using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.MasterData;

namespace Embm.EFiscal.Api.Modules.Documents;

internal record DocumentLineInput(Guid ItemId, Guid? UnitId, string? Quantity, string? UnitPrice, Guid? TaxGroupId,
    Guid[]? DiscountGroupIds, EligibilityProof? Eligibility, Guid? SourceLineId = null, string? AdjustmentAmount = null,
    string? Description = null, Guid? ProjectId = null, Guid? DepartmentId = null, Guid? LocationId = null,
    Guid? AccountId = null, JsonElement? Custom = null, string? ExternalLineKey = null);
internal record DocumentInput(string? Action, DateOnly Date, Guid BranchId, Guid? CustomerId, string? Currency, Guid? PriceListId,
    DocumentLineInput[]? Lines, Guid[]? DocumentDiscountIds = null, Guid? SourceDocumentId = null, string? ExternalOrderKey = null,
    string? Recipient = null, Guid? PdfTemplateId = null, Guid? EmailTemplateId = null, string? Remarks = null,
    string? AdjustmentReason = null, string? AdjustmentKind = null, DateOnly? RequestedDeliveryDate = null,
    string? ShippingAddress = null, string? Salesperson = null, string? PurchaseOrderReference = null,
    string? ExternalReference = null, string? WarehouseReference = null, bool AllowPartial = false, JsonElement? Custom = null, Guid? PaymentTermId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? HistoricalInvoiceReferenceId = null);
internal record CommitInput(string? Key, string? Confirmation, DocumentInput? Document);
internal record SeriesInput(Guid? BranchId, DateOnly EffectiveFrom, string? Prefix, int Digits, long InitialNumber, int ExpectedRevision);
internal record ResendInput(string? Key, string? Recipient);

internal record PreparedDocument(FiscalDocument Document, DocumentNumberSeries? Series, DocumentAllocation[] Allocations, string Confirmation, string[] Recipients);
internal record TemplateStamp<T>(Guid? Id, int Version, string Name, T Content);
internal record DocumentTemplates(TemplateStamp<PdfLayout>? Pdf, TemplateStamp<EmailTemplate>? Email);
internal record DeliveryStamp(string? Recipient, int? SenderRevision, SenderProfile? Sender, string[] AuthorizedRecipients);
internal record PartySnapshot(string Code, string Name, string? TradeName, string? Tin, string? Rdo, string? RegisteredAddress,
    string? BillingAddress, string? ShippingAddress, string? Contact, string? Email, string? VatStatus, int? Revision = null,
    string? Classification = null);
internal record DocumentParties(PartySnapshot Seller, PartySnapshot Customer, string BranchCode, string BranchName, string BranchAddress);
internal record DocumentHeader(string? Remarks, string? AdjustmentReason, string? AdjustmentKind, DateOnly? RequestedDeliveryDate,
    string? ShippingAddress, string? Salesperson, string? PurchaseOrderReference, string? ExternalReference, string? WarehouseReference,
    string? SourceNumber, object? PaymentTerm, object? ExchangeRate, object Custom, object? ExternalSource = null);

internal static class DocumentRules
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static readonly string[] Types = ["SalesInvoice", "CreditMemo", "SalesOrder"];
    public static bool SalesOrder(string type) => type == "SalesOrder";
    public static string Action(string type) => SalesOrder(type) ? "CommitOnly" : "CommitAndSend";
    public static string TemplateType(string type) => type == "CreditMemo" ? "credit-memo" : "invoice";
    public static string Name(string type) => type switch { "CreditMemo" => "Credit Memo", "SalesOrder" => "Sales Order", _ => "Sales Invoice" };
    public static string Permission(string type, bool write) => (type, write) switch
    {
        ("SalesInvoice", false) => "Invoice.View",
        ("SalesInvoice", true) => "Invoice.SaveAndSend",
        ("CreditMemo", false) => "CreditMemo.View",
        ("CreditMemo", true) => "CreditMemo.SaveAndSend",
        ("SalesOrder", false) => "SalesOrder.View",
        _ => "SalesOrder.Save"
    };
    public static string Key(string? value)
    {
        MasterRegistry.Need(value is { Length: >= 16 and <= 80 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'),
            "key", "Use a stable operation key of 16 to 80 letters, digits, hyphens or underscores.");
        return value!;
    }

    public static JsonElement Freeze<T>(T value) => JsonSerializer.SerializeToElement(value, Json);
    public static T Read<T>(JsonElement value) => value.Deserialize<T>(Json) ?? throw new InvalidOperationException("The committed snapshot is incomplete.");

    public static string Digest<T>(T value)
        => Convert.ToHexString(SHA256.HashData(CanonicalBytes(value)));

    internal static byte[] CanonicalBytes<T>(T value)
    {
        var json = Freeze(value);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, json);
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var element in value.EnumerateArray()) WriteCanonical(writer, element);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    public static string Money(decimal number) => MasterSchema.Money(number);
    public static string PrintMoney(FiscalDocument document, decimal value) =>
        (document.Currency == "PHP" ? "₱" : document.Currency + " ") + value.ToString("N" + document.Precision, CultureInfo.InvariantCulture);
}

