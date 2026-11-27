using System.Globalization;
using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.MasterData;

internal record MasterField(string Key, string Label, string Type = "text", bool Required = false, string[]? Choices = null,
    string? Reference = null, int MaxLength = 240);
internal record MasterDefinition(string Key, string Label, string Read, string Write, bool Import, MasterField[] Fields,
    bool Effective = false);

internal static class MasterSchema
{
    public const string Export = "MasterData.Export";
    public const string Calculate = "MasterData.Calculate";
    public const string View = "MasterData.View";
    public const string Configure = "FinancialConfiguration.Manage";
    public const string Custom = "CustomFields.Manage";
    private static MasterField F(string key, string label, string type = "text", bool req = false, int max = 240) => new(key, label, type, req, MaxLength: max);
    private static MasterField E(string key, string label, bool req, params string[] choices) => new(key, label, "choice", req, choices);
    private static MasterField R(string key, string label, string kind, bool req = false) => new(key, label, "reference", req, Reference: kind);

    private static MasterField[] Defaults() => [
        R("taxGroup", "Default tax group", "tax-groups"), R("discountGroup", "Default discount group", "discount-groups"),
        R("priceList", "Default price list", "price-lists"), R("paymentTerm", "Default payment term", "payment-terms")];

    private static MasterField[] Dates() => [F("effectiveFrom", "Effective from", "date", true), F("effectiveThrough", "Effective through", "date")];
    public static readonly MasterDefinition[] Definitions =
    [
        new("customers", "Customers", "Customers.View", "Customers.Manage", true,
        [
            E("classification", "Customer classification", true, "Individual", "Corporate"), F("registeredName", "Registered business name"),
            F("tradeName", "Trade name"), F("tin", "TIN", max: 24), F("rdo", "Registered RDO", "rdo"),
            E("vatStatus", "VAT registration", false, "VAT", "NonVAT", "Exempt", "Unspecified"),
            F("registeredAddress", "Registered address", "multiline", max: 600), F("billingAddress", "Billing address", "multiline", max: 600),
            F("shippingAddress", "Shipping address", "multiline", max: 600), F("contact", "Contact person"), F("email", "Email", "email"),
            F("invoiceRecipients", "Additional invoice emails", "emails"), F("mobile", "Mobile number", max: 40), F("telephone", "Telephone", max: 40),
            R("group", "Customer group", "customer-groups"), R("currency", "Default currency", "currencies"),
            ..Defaults(), F("custom", "Custom fields", "custom-values"), F("external", "External accounting references", "external")
        ]),
        new("customer-groups", "Customer Groups", "Customers.View", "Customers.Manage", false,
            [F("description", "Description", "multiline", max: 600), ..Defaults()]),
        new("items", "Items and Services", "Items.View", "Items.Manage", true,
        [
            E("itemType", "Item type", true, "Goods", "Services"), F("description", "Description", "multiline", max: 600),
            R("category", "Item category", "item-categories"), R("unit", "Base unit", "units", true),
            F("price", "Default selling price", "decimal", true), R("currency", "Currency", "currencies"),
            R("taxGroup", "Default tax group", "tax-groups"), R("discountGroup", "Default discount group", "discount-groups"),
            R("account", "Accounting account reference", "dimensions"), F("barcode", "Barcode", max: 64),
            F("custom", "Custom fields", "custom-values"), F("external", "External item references", "external")
        ]),
        new("item-categories", "Item Categories", "Items.View", "Items.Manage", false, [F("description", "Description")]),
        new("units", "Units of Measure", "Items.View", "Items.Manage", false, [F("symbol", "Symbol", max: 24)]),
        new("item-units", "Item Unit Conversions", "Items.View", "Items.Manage", false,
            [R("item", "Item", "items", true), R("unit", "Alternate unit", "units", true), F("factor", "Base units per alternate unit", "decimal", true)]),
        new("tax-groups", "Tax Groups", "TaxGroups.View", "TaxGroups.Manage", false,
        [
            F("description", "Description", "multiline", max: 600),
            E("classification", "Classification", true, "VATable", "VATExempt", "ZeroRated", "NonVAT", "Other"),
            E("treatment", "Price treatment", true, "Exclusive", "Inclusive"), E("rounding", "Rounding", true, "HalfAway", "HalfEven"),
            F("components", "Tax components", "components"), R("category", "Applicable item category", "item-categories"),
            R("customerGroup", "Applicable customer group", "customer-groups"),
            F("mapping", "Accounting tax mapping reference"), F("authority", "Regulatory template", "authority"),
            F("source", "Company regulatory reference", "multiline", max: 600), F("companyAuthorized", "Authorize this verified rule for the company", "boolean"), ..Dates()
        ], true),
        new("discount-groups", "Discount Groups", "DiscountGroups.View", "DiscountGroups.Manage", false,
        [
            E("category", "Discount category", true, "Commercial", "SeniorCitizen", "PWD", "SoloParent", "AthleteCoach", "OtherStatutory"),
            E("method", "Calculation method", true, "Percentage", "Fixed"), F("amount", "Rate % or fixed amount", "decimal", true),
            F("maximum", "Maximum discount", "decimal"), F("minimumQuantity", "Minimum quantity", "decimal"),
            E("scope", "Scope", true, "Line", "Document"), F("priority", "Calculation priority", "integer", true),
            E("combination", "Combination rule", true, "Stackable", "Exclusive"),
            R("item", "Eligible item", "items"), R("itemCategory", "Eligible item category", "item-categories"),
            R("customer", "Eligible customer", "customers"), R("customerGroup", "Eligible customer group", "customer-groups"),
            F("evidence", "Eligibility or evidence instructions", "multiline", max: 600),
            E("taxTreatment", "Tax treatment", true, "ReduceBase", "VATExempt"), F("authority", "Regulatory template", "authority"),
            F("source", "Company regulatory reference", "multiline", max: 600), F("companyAuthorized", "Authorize this verified rule for the company", "boolean"), ..Dates()
        ], true),
        new("price-lists", "Price Lists", "PriceLists.View", "PriceLists.Manage", false,
            [R("currency", "Currency", "currencies"), F("description", "Description"), ..Dates()], true),
        new("price-entries", "Price List Entries", "PriceLists.View", "PriceLists.Manage", false,
        [
            R("priceList", "Price list", "price-lists", true), R("item", "Item", "items", true), R("unit", "Unit", "units", true),
            F("price", "Selling price", "decimal", true), F("minimumQuantity", "Minimum quantity", "decimal"),
            R("customerGroup", "Customer group", "customer-groups"), R("customer", "Customer", "customers"), ..Dates()
        ], true),
        new("payment-terms", "Payment Terms", View, Configure, false,
            [F("description", "Description"), F("days", "Number of days", "integer", true), E("method", "Due date method", true, "Immediate", "CalendarDays", "MonthEnd")]),
        new("currencies", "Currencies", View, Configure, false,
            [F("symbol", "Currency symbol", max: 12), F("precision", "Decimal places", "integer", true)]),
        new("exchange-rates", "Exchange Rates", View, Configure, false,
            [R("currency", "Foreign currency", "currencies", true), F("rate", "PHP per unit", "decimal", true),
             F("date", "Exchange rate date", "date", true), F("source", "Authorized source", req: true)]),
        new("dimensions", "Accounting Dimensions", View, Configure, false,
            [E("dimensionType", "Dimension type", true, "Project", "Department", "Location", "AccountReference", "Other"),
             F("description", "Description"), F("externalCode", "External mapping code", max: 120)]),
        new("custom-fields", "Custom Fields", View, Custom, false,
        [
            F("recordTypes", "Applicable record types", "record-types", true),
            E("valueType", "Field type", true, "Text", "Integer", "Decimal", "Date", "Boolean", "Dropdown", "Reference"),
            F("required", "Required", "boolean"), F("default", "Default value"), F("displayOrder", "Display order", "integer", true),
            F("values", "Allowed dropdown values", "list"), E("referenceType", "Authorized reference type", false, "customers", "items", "dimensions", "units"),
            F("maximumLength", "Maximum text length", "integer"), F("minimum", "Minimum numeric value", "decimal"), F("maximum", "Maximum numeric value", "decimal")
        ])
    ];

    public static readonly string[] RecordTypes = ["customers", "items", "sales-invoice-header", "sales-invoice-line",
        "credit-memo-header", "credit-memo-line", "sales-order"];
    public static string ImportPermission(string kind) => kind == "customers" ? "Customers.Import" : "Items.Import";

    public static string? Str(this JsonElement data, string key) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static decimal Num(this JsonElement data, string key, decimal fallback = 0) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.TryGetDecimal(out var number) ? number : fallback;
    public static DateOnly? Day(this JsonElement data, string key) => DateOnly.TryParseExact(data.Str(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
    public static Guid? Ref(this JsonElement data, string key) => Guid.TryParse(data.Str(key), out var id) ? id : null;
    public static bool Flag(this JsonElement data, string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
    public static JsonElement[] Array(this JsonElement data, string key) => data.ValueKind == JsonValueKind.Object && data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Array
        ? value.EnumerateArray().ToArray() : [];
    public static bool DecimalText(string? value, out decimal number)
    {
        number = 0;
        return value is { Length: > 0 and <= 30 } && value.AsSpan().IndexOfAny(' ', '\t', ',') < 0 &&
            decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)
            && Math.Abs(number) <= 999999999999.999999m && decimal.Round(number, 8) == number;
    }
    public static string Money(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
}

