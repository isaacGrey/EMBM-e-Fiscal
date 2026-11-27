using System.Globalization;
using System.Text.Json;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed record ComparisonCell(string Field, string? Line, string Category, string State,
    JsonElement? Expected, JsonElement? Actual, decimal? Difference = null);
internal sealed record ComparisonResult(string Status, string Completeness, ComparisonCell[] Cells);

internal static class ReconciliationComparison
{
    private static readonly string[] HeaderIdentity = ["documentType", "date", "customer", "currency", "exchangeRate", "status"];
    private static readonly string[] HeaderMoney = ["grossSales", "vatableSales", "exemptSales", "zeroRatedSales", "nonVatSales", "total", "amountDue",
        "rounding", "incomeWithholding", "vatWithholding", "vat", "commercialDiscount", "statutoryDiscount"];
    private static readonly string[] LineIdentity = ["item", "unit", "quantity", "unitPrice", "description", "taxClassification"];
    private static readonly string[] LineMoney = ["gross", "vatableSales", "exemptSales", "zeroRatedSales", "nonVatSales", "total", "incomeWithholding",
        "vatWithholding", "vat", "commercialDiscount", "statutoryDiscount"];

    public static ComparisonResult Compare(AccountingValues expected, AccountingValues actual, decimal approvedTolerance)
    {
        List<ComparisonCell> cells = [];
        var currency = expected.Header.GetValueOrDefault("currency");
        var tolerance = currency.ValueKind == JsonValueKind.String && currency.GetString() == "PHP" &&
            actual.Header.TryGetValue("currency", out var actualCurrency) && JsonElement.DeepEquals(currency, actualCurrency) ? approvedTolerance : 0m;

        foreach (var name in HeaderIdentity.Concat(HeaderMoney)) CompareField(name, null, expected.Header, actual.Header);
        foreach (var name in new[] { "relationship", "paymentTerm", "customHeader" })
            if (expected.Header.ContainsKey(name) || actual.Header.ContainsKey(name)) CompareField(name, null, expected.Header, actual.Header);
        foreach (var name in new[] { "customerName", "customerTin", "dueDate", "branch", "shipping" })
            cells.Add(new(name, null, "UnsupportedComparison", "Unsupported", null, null));

        if (actual.Lines is null || expected.Lines is null)
            cells.Add(new("lines", null, "UnsupportedComparison", "Unverified", null, null));
        else
        {
            var expectedLines = expected.Lines.ToLookup(x => x.Reference);
            var actualLines = actual.Lines.ToLookup(x => x.Reference);
            foreach (var key in expectedLines.Select(x => x.Key).Union(actualLines.Select(x => x.Key)).Order(StringComparer.Ordinal))
            {
                var left = expectedLines[key].ToArray();
                var right = actualLines[key].ToArray();
                if (left.Length != 1 || right.Length != 1 || string.IsNullOrEmpty(key))
                {
                    var field = right.Length > 1 || left.Length > 1 ? "duplicateLine" : right.Length == 0 ? "missingLine" : "additionalLine";
                    cells.Add(new(field, key, "LineMismatch", "Mismatch", Freeze(left.Length), Freeze(right.Length)));
                    continue;
                }
                foreach (var name in LineIdentity.Concat(LineMoney)) CompareField(name, key, left[0].Values, right[0].Values);
                foreach (var name in new[] { "taxCode", "discountCode", "account", "project", "department", "location", "customLine" })
                    if (left[0].Values.ContainsKey(name) || right[0].Values.ContainsKey(name))
                        CompareField(name, key, left[0].Values, right[0].Values);
            }
        }

        var mismatches = cells.Where(x => x.State == "Mismatch").ToArray();
        var incomplete = cells.Any(x => x.State == "Unverified");
        var precedence = new[] { "RelationshipMismatch", "HeaderMismatch", "LineMismatch", "TaxMismatch", "DiscountMismatch", "FinancialMismatch" };
        var status = mismatches.Length > 0 ? precedence.First(x => mismatches.Any(m => m.Category == x)) :
            incomplete ? "PartialVerification" : cells.Any(x => x.State == "WithinTolerance") ? "MatchedWithinTolerance" : "Matched";
        return new(status, incomplete ? "Partial" : "Complete", [..cells]);

        void CompareField(string name, string? line, Dictionary<string, JsonElement> left, Dictionary<string, JsonElement> right)
        {
            JsonElement? a = left.TryGetValue(name, out var e) ? e : null;
            JsonElement? b = right.TryGetValue(name, out var v) ? v : null;
            var category = name switch
            {
                "relationship" => "RelationshipMismatch",
                "vat" or "vatableSales" or "exemptSales" or "zeroRatedSales" or "nonVatSales" or "incomeWithholding" or
                    "vatWithholding" or "taxClassification" or "taxCode" => "TaxMismatch",
                "commercialDiscount" or "statutoryDiscount" or "discountCode" => "DiscountMismatch",
                _ when line is not null => "LineMismatch",
                _ when HeaderMoney.Contains(name) => "FinancialMismatch",
                _ => "HeaderMismatch"
            };
            if (a is null || b is null)
            {
                cells.Add(new(name, line, category, "Unverified", a, b));
                return;
            }
            if (TryDecimal(a.Value, out var before) && TryDecimal(b.Value, out var after))
            {
                var delta = after - before;
                var tolerated = name is ("total" or "amountDue" or "rounding") && line is null && Math.Abs(delta) <= tolerance;
                cells.Add(new(name, line, category, delta == 0 ? "Matched" : tolerated ? "WithinTolerance" : "Mismatch", a, b, delta));
                return;
            }
            cells.Add(new(name, line, category, JsonElement.DeepEquals(a.Value, b.Value) ? "Matched" : "Mismatch", a, b));
        }
    }

    private static bool TryDecimal(JsonElement element, out decimal amount)
    {
        if (element.ValueKind == JsonValueKind.Number) return element.TryGetDecimal(out amount);
        amount = 0m;
        return false;
    }
}

