using System.Globalization;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Documents;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record MappingField(string Code, string Label, string Scope, string Type, bool Required = false, bool Financial = false,
    int MaxLength = 200, int Scale = 8, string? Reference = null, string[]? Allowed = null, bool ReadOnly = false,
    bool FinancialReference = false);
internal sealed record FieldRule(string Scope, string Target, string Operation, string[]? Sources = null, JsonElement? Value = null,
    JsonElement? Fallback = null, Dictionary<string, JsonElement>? Values = null);
internal sealed record MappingValues(Dictionary<string, object?> Header, Dictionary<string, object?>[] Lines);
internal sealed record BoundReference(string Kind, string LocalKey, string ExternalId, string ExternalCode);
internal sealed record MappingDefinition(MappingField[] Source, MappingField[] Destination);

internal static class FieldMapper
{
    public const string Outbound = "Outbound";
    public const string Inbound = "Inbound";
    public const string Response = "Response";
    public static readonly string[] Directions = [Outbound, Inbound, Response];
    public static readonly string[] Operations = ["copy", "constant", "default", "concat", "trim", "upper", "lower", "date", "decimal", "boolean",
        "enum", "lookup", "if", "omit", "reference", "object"];
    private static MappingField Field(string code, string label, string scope, string type = "text", bool required = false, bool financial = false,
        string? reference = null, string[]? allowed = null, bool financialReference = false) =>
        new(code, label, scope, type, required, financial, Reference: reference, Allowed: allowed, FinancialReference: financialReference);
    private static readonly MappingField[] HeaderMoney = new[] { "grossSales", "commercialDiscount", "statutoryDiscount", "vatableSales",
        "exemptSales", "zeroRatedSales", "nonVatSales", "vat", "incomeWithholding", "vatWithholding", "total", "amountDue", "rounding" }
        .Select(x => Field(x, x, "Header", "decimal", true, true)).ToArray();
    private static readonly MappingField[] LineMoney = new[] { "quantity", "unitPrice", "gross", "commercialDiscount", "statutoryDiscount", "vatableSales",
        "exemptSales", "zeroRatedSales", "nonVatSales", "vat", "incomeWithholding", "vatWithholding", "total", "baseFactor" }
        .Select(x => Field(x, x, "Line", "decimal", true, true)).ToArray();
    private static readonly MappingField[] DocumentFields =
    [
        Field("documentType", "Document type", "Header", required: true, financial: true, allowed: DocumentRules.Types),
        Field("number", "eFiscal number", "Header", required: true, financial: true),
        Field("date", "Document date", "Header", "date", true, true),
        Field("currency", "Currency code", "Header", required: true, financial: true),
        Field("exchangeRate", "Exchange rate", "Header", "decimal", true, true),
        Field("customer", "Customer identifier", "Header", required: true, reference: "Customer"),
        Field("relationship", "Related external document", "Header"),
        Field("remarks", "Remarks", "Header"),
        Field("paymentTerm", "Payment term", "Header", reference: "PaymentTerm"),
        Field("currencyReference", "External currency", "Header", reference: "Currency", financialReference: true),
        Field("customHeader", "Custom header fields", "Header", "object"),
        ..HeaderMoney,
        Field("position", "Original line position", "Line", "integer", true, true),
        Field("item", "Item identifier", "Line", required: true, reference: "Item"),
        Field("unit", "Unit of measure", "Line", required: true, reference: "Unit"),
        Field("description", "Description", "Line"),
        Field("taxClassification", "Committed tax classification", "Line", required: true, financial: true),
        Field("taxCode", "External tax code", "Line", reference: "Tax", financialReference: true),
        Field("discountCode", "External discount code", "Line", reference: "Discount", financialReference: true),
        Field("account", "Account", "Line", reference: "Account"),
        Field("project", "Project", "Line", reference: "Project"),
        Field("department", "Department", "Line", reference: "Department"),
        Field("location", "Location", "Line", reference: "Location"),
        Field("customLine", "Custom line fields", "Line", "object"),
        ..LineMoney
    ];

    private static readonly MappingField[] OrderFields =
    [
        Field("reference", "Source reference", "Header", required: true),
        Field("orderedOn", "Source date", "Header", "date", true),
        Field("currency", "Source currency", "Header", required: true),
        Field("customer", "Source customer", "Header", required: true, reference: "Customer"),
        Field("shipping", "Source shipping charge", "Header", "decimal"),
        Field("refund", "Source refund", "Header", "decimal"),
        Field("cancelled", "Cancellation signal", "Header", "boolean"),
        Field("item", "Source item", "Line", required: true, reference: "Item"),
        Field("quantity", "Source quantity", "Line", "decimal", true),
        Field("price", "Source price", "Line", "decimal"),
        Field("discount", "Source discount", "Line", "decimal"),
        Field("taxCode", "Source tax reference", "Line", reference: "Tax")
    ];
    private static readonly MappingField[] ResponseFields =
    [
        Field("externalId", "External record ID", "Header", required: true),
        Field("externalNumber", "External document number", "Header", required: true),
        Field("status", "External record status", "Header", required: true, allowed: ["Posted", "Accepted", "Found"])
    ];

    public static MappingDefinition Describe(string direction)
    {
        var fields = direction switch { Outbound => DocumentFields, Inbound => OrderFields, Response => ResponseFields, _ => [] };
        return new(fields, fields);
    }
    public static string Operation(string type, string direction) => direction switch
    {
        Inbound => "SourceOrderRead", Response => "Normalize" + type, _ => type + "Create"
    };

    public static FieldRule[] Defaults(string direction) => Describe(direction).Destination
        .Select(f => new FieldRule(f.Scope, f.Code, f.FinancialReference ? "reference" : "copy", [f.Code])).ToArray();
    public static string[] Validate(string direction, FieldRule[] rules)
    {
        Require(Directions.Contains(direction) && rules.Length <= 100 && rules.All(x => x is not null), "The mapping exceeds its allowed shape.");
        var definition = Describe(direction);
        List<string> errors = [];
        foreach (var field in definition.Destination.Where(x => x.Required))
            if (!rules.Any(r => r.Scope == field.Scope && r.Target == field.Code)) errors.Add("Required field is unmapped: " + field.Code);
        if (rules.GroupBy(x => (x.Scope, x.Target)).Any(x => x.Count() > 1)) errors.Add("Each destination field may be mapped once.");
        foreach (var rule in rules)
        {
            var target = definition.Destination.SingleOrDefault(f => f.Code == rule.Target && f.Scope == rule.Scope);
            var sources = rule.Sources ?? [];
            var shape = rule.Operation switch
            {
                "constant" => sources.Length == 0 && rule.Value.HasValue,
                "omit" => sources.Length == 0,
                "concat" or "object" => sources.Length is >= 1 and <= 8,
                "if" => sources.Length == 1 && rule.Value.HasValue && rule.Fallback.HasValue,
                "enum" or "lookup" => sources.Length == 1 && rule.Values is { Count: >= 1 and <= 60 },
                "default" => sources.Length == 1 && rule.Fallback.HasValue,
                "copy" or "trim" or "upper" or "lower" or "date" or "decimal" or "boolean" or "reference" => sources.Length == 1,
                _ => false
            };
            if (target is null || target.ReadOnly || !shape || sources.Any(s => !definition.Source.Any(f => f.Code == s && f.Scope == rule.Scope)))
            { errors.Add("Unsupported rule or source for " + (target?.Code ?? "unknown field")); continue; }
            var src = sources.Length == 1 ? definition.Source.Single(f => f.Code == sources[0] && f.Scope == rule.Scope) : null;
            if (target.Required && rule.Operation == "omit" || target.Reference is null && rule.Operation == "reference" ||
                target.Financial && (rule.Operation is not ("copy" or "decimal" or "date") || src?.Code != target.Code) ||
                target.FinancialReference && rule.Operation != "omit" && (rule.Operation != "reference" || src?.Code != target.Code) ||
                rule.Operation == "copy" && target.Type != src?.Type ||
                rule.Operation == "object" && target.Type != "object" ||
                target.Type == "object" && rule.Operation is not ("object" or "copy" or "omit") ||
                rule.Operation is ("trim" or "upper" or "lower" or "concat") && target.Type != "text")
                errors.Add("Incompatible or protected field: " + target.Code);
            try
            {
                if (rule.Operation == "constant" && rule.Value is JsonElement literal) _ = ConvertTo(literal, target);
                if (rule.Operation == "default" && rule.Fallback is JsonElement fallback) _ = ConvertTo(fallback, target);
                if (rule.Operation == "if" && rule.Fallback is JsonElement other) _ = ConvertTo(other, target);
                foreach (var pair in rule.Values ?? []) { _ = SpecImport.Text(pair.Key, 120); _ = ConvertTo(pair.Value, target); }
            }
            catch (ConnectorInputError) { errors.Add("Invalid value type or limit for " + target.Code); }
        }
        return errors.Distinct().Take(100).ToArray();
    }

    public static MappingValues Map(string direction, FieldRule[] rules, MappingValues document, BoundReference[] references)
    {
        if (Validate(direction, rules) is { Length: > 0 } errors) throw new ConnectorInputError(errors[0]);
        Require(document.Lines.Length <= 100 && references.Length <= 1500, "The mapping exceeds the line or reference limit.");
        var definition = Describe(direction);
        var time = System.Diagnostics.Stopwatch.StartNew();
        var output = new MappingValues(Project("Header", document.Header), document.Lines.Select(line => Project("Line", line)).ToArray());
        Require(Freeze(output).GetRawText().Length <= 256_000, "The mapped payload exceeds its size limit.");
        if (direction == Outbound) CheckMoney(definition, document, output);
        return output;

        Dictionary<string, object?> Project(string scope, Dictionary<string, object?> input)
        {
            Dictionary<string, object?> result = [];
            foreach (var rule in rules.Where(x => x.Scope == scope))
            {
                Require(time.ElapsedMilliseconds <= 1500, "The mapping exceeded its processing limit.");
                var field = definition.Destination.Single(f => f.Scope == scope && f.Code == rule.Target);
                var sources = (rule.Sources ?? []).Select(key => input.GetValueOrDefault(key)).ToArray();
                var value = sources.FirstOrDefault();
                var text = StringValue(value);
                object? computed = rule.Operation switch
                {
                    "constant" => rule.Value,
                    "default" => value ?? rule.Fallback,
                    "concat" => string.Concat(sources.Select(StringValue)),
                    "trim" => value is null ? null : text.Trim(),
                    "upper" => value is null ? null : text.ToUpperInvariant(),
                    "lower" => value is null ? null : text.ToLowerInvariant(),
                    "enum" or "lookup" => value is null ? null : rule.Values!.TryGetValue(text, out var hit) ? hit :
                        throw new ConnectorInputError("Unmapped lookup value for " + field.Code),
                    "if" => text == StringValue(rule.Value) ? value : rule.Fallback,
                    "omit" => null,
                    "reference" => string.IsNullOrEmpty(text) ? null : ResolveReference(direction, references, field, text),
                    "boolean" when text is "1" or "0" => text == "1",
                    "object" => (rule.Sources ?? []).Distinct().ToDictionary(x => x, x => input.GetValueOrDefault(x)),
                    _ => value
                };
                var converted = ConvertTo(computed, field);
                if (converted is not null) result[field.Code] = converted;
                else if (field.Required) throw new ConnectorInputError("Required value is missing: " + field.Code);
            }
            return result;
        }
    }

    private static string? ResolveReference(string direction, BoundReference[] references, MappingField field, string value)
    {
        var match = references.SingleOrDefault(x => x.Kind == field.Reference && (direction == Inbound ? x.ExternalId == value : x.LocalKey == value));
        if (match is not null) return direction == Inbound ? match.LocalKey : match.ExternalId;
        if (field.FinancialReference) return null;
        throw new ConnectorInputError("Unmapped company reference: " + field.Reference);
    }

    private static object? ConvertTo(object? value, MappingField field)
    {
        if (value is null || value is JsonElement { ValueKind: JsonValueKind.Null }) return null;
        var text = StringValue(value);
        switch (field.Type)
        {
            case "text":
                if (value is string or JsonElement { ValueKind: JsonValueKind.String or JsonValueKind.Number } || value is Guid)
                {
                    var clean = SpecImport.Text(text, field.MaxLength);
                    if (field.Allowed is null || field.Allowed.Contains(clean)) return clean;
                }
                break;
            case "decimal":
                if (decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d)
                    && decimal.Round(d, field.Scale) == d) return d;
                break;
            case "integer":
                if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return n;
                break;
            case "date":
                if (value is DateOnly day) return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (DateOnly.TryParseExact(text, ["yyyy-MM-dd", "yyyyMMdd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                break;
            case "boolean":
                if (bool.TryParse(text, out var boolean)) return boolean;
                break;
            case "object":
                var serialized = value is JsonElement json ? json : Freeze(value);
                if (serialized.ValueKind == JsonValueKind.Object && serialized.GetRawText().Length <= 4096) return serialized;
                break;
        }
        throw new ConnectorInputError("A value is incompatible with the field " + field.Code);
    }

    private static string StringValue(object? value) => value switch
    {
        null => "", JsonElement { ValueKind: JsonValueKind.String } text => text.GetString() ?? "",
        JsonElement x => x.ToString(), IFormattable f => f.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString() ?? ""
    };

    private static void CheckMoney(MappingDefinition definition, MappingValues original, MappingValues mapped)
    {
        foreach (var field in definition.Destination.Where(x => x.Financial))
        {
            var scope = field.Scope == "Header" ? new[] { original.Header } : original.Lines;
            var output = field.Scope == "Header" ? new[] { mapped.Header } : mapped.Lines;
            Require(scope.Length == output.Length, "The mapping changed the number of committed lines.");
            for (var i = 0; i < scope.Length; i++)
                Require(Equals(ConvertTo(scope[i].GetValueOrDefault(field.Code), field), output[i].GetValueOrDefault(field.Code)),
                    "The mapping changed the committed value " + field.Code);
        }
    }

    public static MappingValues Document(FiscalDocument doc, string? relationship = null)
    {
        var source = Freeze(new { documentType = doc.Type, doc.Number, doc.Date, doc.Currency, doc.ExchangeRate,
            customer = doc.CustomerId.ToString(), relationship,
            doc.GrossSales, doc.CommercialDiscount, doc.StatutoryDiscount, doc.VatableSales, doc.ExemptSales, doc.ZeroRatedSales,
            doc.NonVatSales, doc.Vat, doc.IncomeWithholding, doc.VatWithholding, doc.Total, doc.AmountDue, doc.Rounding });
        var h = ReadFields(source);
        var header = doc.Snapshot?.Header ?? default;
        h["remarks"] = SpecImport.Str(header, "remarks");
        h["customHeader"] = SpecImport.Get(header, "custom");
        h["currencyReference"] = doc.Currency;
        if (SpecImport.Get(header, "paymentTerm") is { } term) h["paymentTerm"] = SpecImport.Str(term, "id");
        return new(h, doc.Lines.OrderBy(x => x.Position).Select(line =>
        {
            var values = ReadFields(Freeze(new { line.Position, item = line.ItemId.ToString(), unit = line.UnitId.ToString(),
                line.Description, line.Quantity, line.UnitPrice, line.Gross, line.CommercialDiscount, line.StatutoryDiscount,
                line.VatableSales, line.ExemptSales, line.ZeroRatedSales, line.NonVatSales, line.Vat,
                line.IncomeWithholding, line.VatWithholding, line.Total, line.BaseFactor }));
            var terms = line.Terms;
            var original = SpecImport.Get(terms, "originalTerms") ?? terms;
            values["taxClassification"] = SpecImport.Str(original, "classification") is var classification && classification != "" ? classification : "Recorded";
            if (SpecImport.Get(original, "rule") is { } tax) values["taxCode"] = SpecImport.Str(tax, "id");
            if (SpecImport.Array(SpecImport.Get(original, "discounts")).FirstOrDefault() is { ValueKind: JsonValueKind.Object } discount &&
                SpecImport.Get(discount, "rule") is { } rule) values["discountCode"] = SpecImport.Str(rule, "id");
            var extra = SpecImport.Get(terms, "extensions") ?? SpecImport.Get(terms, "extra") ?? default;
            foreach (var dimension in new[] { "account", "project", "department", "location" })
                if (SpecImport.Get(extra, dimension) is { ValueKind: JsonValueKind.Object } reference) values[dimension] = SpecImport.Str(reference, "id");
            values["customLine"] = SpecImport.Get(extra, "custom");
            return values;
        }).ToArray());
    }

    public static Dictionary<string, object?> ReadFields(JsonElement json) =>
        SpecImport.Entries(json).ToDictionary(x => x.Name, x => (object?)x.Value);
    public static MappingValues Order(ProviderOrder source) => new(ReadFields(Freeze(new { source.Reference, source.OrderedOn, source.Currency,
        customer = source.CustomerCode, source.Shipping, source.Refund, source.Cancelled })),
        source.Lines.Select(x => ReadFields(Freeze(new { item = x.ItemCode, quantity = x.InvoiceableQuantity, x.Price, x.Discount, x.TaxCode }))).ToArray());
    public static MappingValues Sample(string type, string direction)
    {
        if (direction == Response) return new(new() { ["externalId"] = "test-record", ["externalNumber"] = "DEV-001", ["status"] = "Posted" }, []);
        if (direction == Inbound) return Order(new("example", "SHOP-1001", new(2026, 1, 1), "PHP", "Open", "DEMO-BUYER", "Example",
            [new("01", "DEMO-SERVICE", "Example", 2, 2, 100, 5, "VAT12")], 25m));
        return Document(new FiscalDocument
        {
            Number = "SAMPLE-0001", Type = type, Date = new(2026, 1, 1), Currency = "PHP", ExchangeRate = 1, GrossSales = 200,
            VatableSales = 200, Vat = 24, Total = 224, AmountDue = 224, CustomerId = Guid.Empty,
            Lines = [new FiscalDocumentLine { Position = 1, Description = "Synthetic service", Quantity = 2, UnitPrice = 100, BaseFactor = 1,
                Gross = 200, VatableSales = 200, Vat = 24, Total = 224 }]
        }, "DEV-ORDER-01");
    }
}

