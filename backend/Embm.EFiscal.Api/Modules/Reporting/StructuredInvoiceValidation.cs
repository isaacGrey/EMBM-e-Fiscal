using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Embm.EFiscal.Api.Modules.Documents;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal static class StructuredInvoiceValidation
{
    private static readonly string[] SummedAmounts = ["commercialDiscount", "statutoryDiscount", "vatableSales",
        "vatExemptSales", "zeroRatedSales", "nonVatSales", "vat", "incomeWithholding", "vatWithholding", "total"];
    private static readonly string[] TaxAmounts = ["vatableSales", "vatExemptSales", "zeroRatedSales", "nonVatSales", "vat"];

    internal static bool HasDigest(ReadOnlySpan<byte> bytes, string digest) =>
        string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), digest, StringComparison.Ordinal);

    internal static bool SchemaCompatible(RegulatorySchemaVersion? schema, string type) =>
        schema?.Classification == "InternalCanonical" && schema.Environment == "Internal" && schema.Verification == "InternalOnly" &&
        schema.DocumentTypes.Contains(type) && HasDigest(Encoding.UTF8.GetBytes(schema.Content), schema.Sha256);

    internal static string[] Issues(JsonElement payload, Guid id, string type, string number)
    {
        var issues = new List<string>();
        try
        {
            if (payload.GetProperty("schemaVersion").GetString() != CanonicalInvoices.Version ||
                payload.GetProperty("format").GetString() != "InternalCanonical" || !CanonicalInvoices.Supports(type))
                issues.Add("Unsupported canonical format or document type.");

            if (payload.GetProperty("documentedAuthorizationReferences").ValueKind is not (JsonValueKind.Array or JsonValueKind.Null) ||
                payload.GetProperty("evidenceLimitations").EnumerateArray().Any(x => x.ValueKind != JsonValueKind.String))
                issues.Add("Invalid evidence metadata.");

            var fiscal = payload.GetProperty("fiscalSnapshot");
            var document = fiscal.GetProperty("document");
            if (document.GetProperty("id").GetGuid() != id || document.GetProperty("type").GetString() != type ||
                document.GetProperty("number").GetString() != number || string.IsNullOrWhiteSpace(number))
                issues.Add("Document identity differs from the committed record.");
            if (document.GetProperty("seriesId").GetGuid() == Guid.Empty || document.GetProperty("numbering").ValueKind != JsonValueKind.Object ||
                !DateOnly.TryParseExact(document.GetProperty("date").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                !document.GetProperty("committedAt").TryGetDateTimeOffset(out _) ||
                document.GetProperty("precision").GetInt32() is < 0 or > 6 || document.GetProperty("currency").GetString() is not { Length: 3 } ||
                Amount(document, "exchangeRate") <= 0)
                issues.Add("Invalid numbering, dates, currency, or exchange rate.");

            var original = document.GetProperty("originalInvoiceId");
            if (type == "CreditMemo" ? original.GetGuid() is var source && (source == Guid.Empty || source == id) : original.ValueKind != JsonValueKind.Null)
                issues.Add("Invalid original invoice relationship.");

            var seller = fiscal.GetProperty("seller");
            var buyer = fiscal.GetProperty("buyer");
            if (seller.GetProperty("companyId").GetGuid() == Guid.Empty || seller.GetProperty("branchId").GetGuid() == Guid.Empty ||
                string.IsNullOrWhiteSpace(seller.GetProperty("branchCode").GetString()) || string.IsNullOrWhiteSpace(seller.GetProperty("name").GetString()) ||
                buyer.GetProperty("customerId").GetGuid() == Guid.Empty || string.IsNullOrWhiteSpace(buyer.GetProperty("name").GetString()))
                issues.Add("Required seller, branch or buyer identity is missing.");

            var lines = fiscal.GetProperty("lines").EnumerateArray().ToArray();
            if (lines.Length is < 1 or > 100 || !lines.Select(x => x.GetProperty("position").GetInt32()).SequenceEqual(Enumerable.Range(1, lines.Length)) ||
                lines.Select(x => x.GetProperty("id").GetGuid()).Distinct().Count() != lines.Length)
                issues.Add("Line count, identities or order are invalid.");

            foreach (var line in lines)
            {
                if (line.GetProperty("id").GetGuid() == Guid.Empty || line.GetProperty("itemId").GetGuid() == Guid.Empty ||
                    line.GetProperty("unitId").GetGuid() == Guid.Empty || Amount(line, "quantity") < 0 || Amount(line, "unitPrice") < 0 ||
                    Amount(line, "total") < 0 || Amount(line, "total") != TaxAmounts.Sum(key => Amount(line, key)) ||
                    line.GetProperty("taxClassification").ValueKind is not (JsonValueKind.String or JsonValueKind.Null) ||
                    line.GetProperty("financialTerms").ValueKind != JsonValueKind.Object)
                    issues.Add("Invalid identity, taxable amounts or pinned terms on line " + line.GetProperty("position").GetInt32() + ".");
            }

            var totals = fiscal.GetProperty("totals");
            _ = Amount(totals, "rounding");
            if (Amount(totals, "grossSales") != lines.Sum(line => Amount(line, "gross")) ||
                SummedAmounts.Any(key => Amount(totals, key) != lines.Sum(line => Amount(line, key))) ||
                Amount(totals, "amountDue") != Amount(totals, "total") - Amount(totals, "incomeWithholding") - Amount(totals, "vatWithholding") ||
                Amount(totals, "amountDue") < 0)
                issues.Add("Document totals do not match the committed line amounts.");
        }
        catch (Exception error) when (error is FormatException or KeyNotFoundException or InvalidOperationException or OverflowException)
        {
            issues.Add("A required field is missing or an exact decimal, identity or date is invalid.");
        }
        return issues.ToArray();
    }

    private static decimal Amount(JsonElement parent, string key)
    {
        var text = parent.GetProperty(key).GetString();
        if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            DocumentRules.Money(value) != text)
            throw new FormatException("Invalid canonical decimal: " + key);
        return value;
    }
}
