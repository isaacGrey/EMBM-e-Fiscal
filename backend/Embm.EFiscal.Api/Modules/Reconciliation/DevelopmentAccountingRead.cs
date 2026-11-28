using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed record AccountingValues(Dictionary<string, JsonElement> Header, AccountingLine[]? Lines);
internal sealed record AccountingLine(string Reference, Dictionary<string, JsonElement> Values);
internal sealed record RetrievedAccounting(string Id, string Number, string Type, DateOnly? Date, string Source,
    DateTimeOffset? ModifiedAt, AccountingValues Values, string Evidence, Guid? OperationId = null);
internal sealed record AccountingReadResult(string Status, RetrievedAccounting[] Records);

internal sealed class DevelopmentAccountingRead(EfiscalDbContext db, ConnectionRuntime runtime)
{
    public static JsonElement Posted(FiscalDocument doc, MappingValues posted) => Freeze(new AccountingValues(
        NormalizeHeader(posted.Header), doc.Lines.OrderBy(x => x.Position).Zip(posted.Lines, (line, values) =>
            new AccountingLine(line.Id.ToString("N"), Normalize(values))).ToArray()));

    public async Task<AuthorizedProvider?> Authorize(ExternalConnection link, CancellationToken ct)
    {
        if (link.ResourceType != "CompanyFile") return null;
        var provider = await runtime.Active(link.SubscriberId, link.CompanyId, link.Id, ["ExternalDocumentStatusRead"], false, ct);
        if (provider?.Adapter.Key != "mock-accounting" ||
            provider.Connection.ExternalAccountId != link.ExternalAccountId || provider.Connection.ExternalResourceId != link.ExternalResourceId) return null;
        return await db.ExecutionPolicies.AsNoTracking().AnyAsync(p => p.ConnectorVersionId == link.ConnectorVersionId &&
            p.Capability == "ExternalDocumentStatusRead" && p.Method == "GET" && p.Path == "/v1/accounting/status", ct) ? provider : null;
    }

    public async Task<AccountingReadResult> Read(ExternalConnection link, Guid? operationId, string? externalId, string type, CancellationToken ct)
    {
        if (await Authorize(link, ct) is not { } provider) return new("ConnectionUnavailable", []);
        if (operationId is null)
        {
            var fixture = ExternalFixtures(provider, type).Where(r => r.Id == externalId).ToArray();
            if (fixture.Length != 0) return new("Complete", fixture);
        }
        var scope = db.MockAccountingRecords.AsNoTracking().Where(r => r.SubscriberId == link.SubscriberId && r.CompanyId == link.CompanyId &&
            r.ConnectionId == link.Id && r.ExternalType == type);
        var rows = await scope.Where(r => operationId != null && r.OperationId == operationId ||
                operationId == null && r.ExternalId == externalId).Take(3).ToArrayAsync(ct);
        List<RetrievedAccounting> results = [];
        foreach (var row in rows)
        {
            var change = await db.MockAccountingRevisions.AsNoTracking().Where(r => r.SubscriberId == link.SubscriberId &&
                r.CompanyId == link.CompanyId && r.ConnectionId == link.Id && r.OperationId == row.OperationId)
                .OrderByDescending(x => x.Number).FirstOrDefaultAsync(ct);
            if (change?.Scenario == "IncompletePagination") return new("IncompleteRetrieval", []);
            if (change?.Scenario == "Unavailable") return new("ConnectionUnavailable", []);
            if (change?.Scenario == "RateLimit") return new("RateLimited", []);
            if (change?.Scenario == "Missing") continue;
            var values = row.PostedState?.Deserialize<AccountingValues>(SpecImport.Json) ?? new([], null);
            values.Header["total"] = Freeze(row.Total);
            values.Header["status"] = Freeze(row.Status);
            if (row.Relationship is not null) values.Header["relationship"] = Freeze(row.Relationship);
            values = ObserveChanges(values, change?.Scenario);
            var date = values.Header.TryGetValue("date", out var day) && DateOnly.TryParse(day.ToString(), out var parsed) ? parsed : (DateOnly?)null;
            var item = new RetrievedAccounting(row.ExternalId, row.ExternalNumber, row.ExternalType, date, "Efiscal",
                change?.At ?? row.CreatedAt, values, "Scoped accounting store readback", row.OperationId);
            results.Add(change?.Scenario == "WrongIdentity" ? item with { Id = row.ExternalId + "-different" } : item);
            if (change?.Scenario == "Duplicate") results.Add(item with { Id = row.ExternalId + "-duplicate" });
        }
        return new("Complete", [..results]);
    }

    public async Task<(string Status, RetrievedAccounting[] Rows, bool HasMore)> Page(
        ExternalConnection link, DateOnly from, DateOnly through, string? after, int limit, CancellationToken ct)
    {
        if (await Authorize(link, ct) is not { } provider) return ("ConnectionUnavailable", [], false);
        var rows = ExternalFixtures(provider).Where(r => r.Date.HasValue && r.Date.Value >= from && r.Date.Value <= through &&
            (after is null || string.CompareOrdinal(r.Id, after) > 0)).OrderBy(r => r.Id, StringComparer.Ordinal).Take(limit + 1).ToArray();
        return ("Complete", rows.Take(limit).ToArray(), rows.Length > limit);
    }

    public async Task<RetrievedAccounting[]> References(ExternalConnection link, string type, string? query, CancellationToken ct)
    {
        if (await Authorize(link, ct) is not { } provider) return [];
        return ExternalFixtures(provider, type).Where(r => query is null || r.Number.Contains(query, StringComparison.OrdinalIgnoreCase)).Take(25).ToArray();
    }

    internal static bool HasCompleteCreditHistory(RetrievedAccounting invoice) =>
        invoice.Type == "SalesInvoice" && invoice.Values.Header.TryGetValue("creditLedger", out var ledger) &&
        ledger.ValueKind == JsonValueKind.String && ledger.GetString() == "ImmutableDevelopmentLedgerV1";

    private static RetrievedAccounting[] ExternalFixtures(AuthorizedProvider provider, string? type = null)
    {
        var suffix = provider.Connection.ExternalResourceId.EndsWith("ceb", StringComparison.Ordinal) ? "CEB" : "MNL";
        if (type == "SalesOrder") return provider.Adapter.ReadOrders(provider.Connection.ExternalResourceId, null)
            .Select(x => new RetrievedAccounting(x.Id, x.Reference, "SalesOrder", x.OrderedOn, "ApprovedExternal", null,
                new(Normalize(new() { ["currency"] = x.Currency, ["customer"] = x.CustomerCode, ["status"] = x.Status }),
                    x.Lines.Select(l => new AccountingLine(l.Id, Normalize(new() { ["item"] = l.ItemCode, ["description"] = l.Description,
                        ["quantity"] = l.Quantity, ["remainingQuantity"] = l.InvoiceableQuantity, ["unitPrice"] = l.Price, ["taxCode"] = l.TaxCode }))).ToArray()),
                "Authorized Sales Order lookup")).ToArray();

        var rows = new[]
        {
            Fixture("HIST", new(2026, 10, 15), "SalesInvoice", "Adopted"),
            Fixture("CUTOVER", new(2027, 1, 1), "SalesInvoice", "Adopted"),
            Fixture("EXTERNAL", new(2027, 1, 3), "SalesInvoice", "ApprovedExternal"),
            Fixture("CURRENT", new(2027, 1, 10), "CreditMemo", "Adopted"),
            Fixture("AFTER", new(2027, 2, 1), "SalesInvoice", "Adopted")
        };
        return type is null ? [..rows, ..ExternalFixtures(provider, "SalesOrder")] : rows.Where(x => x.Type == type).ToArray();

        RetrievedAccounting Fixture(string id, DateOnly date, string documentType, string source) => new(
            "fixture-" + suffix + "-" + id, "MOCK-" + suffix + "-" + id, documentType, date, source, null,
            new(Normalize(new() { ["date"] = date, ["documentType"] = documentType, ["currency"] = "PHP",
                ["customer"] = "DEMO-BUYER", ["grossSales"] = 210m, ["commercialDiscount"] = 10m,
                ["vat"] = 24m, ["total"] = 224m, ["status"] = "Posted",
                ["creditLedger"] = id == "HIST" ? "ImmutableDevelopmentLedgerV1" : null,
                ["priorAdjustments"] = id == "HIST" ?
                    new[] { new HistoricalAdjustment("prior-credit-" + suffix, "fixture-line-1", 0.5m, 52.50m, 2.50m, 50m, 6m, 56m) } : null }),
                [new("fixture-line-1", Normalize(new() { ["item"] = "DEMO-SERVICE", ["quantity"] = 2m, ["unitPrice"] = 105m,
                    ["gross"] = 210m, ["commercialDiscount"] = 10m, ["vatableSales"] = 200m,
                    ["taxClassification"] = "VATable", ["vat"] = 24m, ["total"] = 224m }))]), "Scoped development accounting fixture");
    }

    private static AccountingValues ObserveChanges(AccountingValues current, string? scenario)
    {
        if (scenario == "Unsupported") return current with { Lines = null };
        if (scenario is "Vat" or "Discount" or "Total" or "Customer")
        {
            var field = scenario switch { "Vat" => "vat", "Discount" => "commercialDiscount", "Total" => "total", _ => "customer" };
            current.Header[field] = scenario == "Customer" ? Freeze("different-customer") : Freeze(-1m);
        }
        if (current.Lines is not { Length: > 0 } lines) return current;
        switch (scenario)
        {
            case "Quantity": lines[0].Values["quantity"] = Freeze(999m); break;
            case "Price": lines[0].Values["unitPrice"] = Freeze(999m); break;
            case "TaxClass": lines[0].Values["taxClassification"] = Freeze("different-tax-class"); break;
            case "MissingLine": return current with { Lines = lines.Skip(1).ToArray() };
            case "ExtraLine": return current with { Lines = [..lines, lines[0] with { Reference = "additional" }] };
            case "DuplicateLine": return current with { Lines = [..lines, lines[0]] };
            case "Reorder": return current with { Lines = lines.Reverse().ToArray() };
        }
        return current;
    }

    internal static Dictionary<string, JsonElement> Normalize(Dictionary<string, object?> values) => values
        .Where(x => x.Value is not null).ToDictionary(x => x.Key, x => Freeze(x.Value));
    internal static Dictionary<string, JsonElement> NormalizeHeader(Dictionary<string, object?> values)
    {
        var normalized = Normalize(values);
        normalized.Remove("remarks");
        return normalized;
    }
}

