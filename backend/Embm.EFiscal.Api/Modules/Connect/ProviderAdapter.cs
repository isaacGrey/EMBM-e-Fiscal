namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record ProviderResource(string Id, string Name, string Type);
internal sealed record ProviderAccount(string Id, string Name, ProviderResource[] Resources);
internal sealed record ProviderField(string Location, string Name, string Type, bool Required, string[]? AllowedValues = null);
internal sealed record ProviderMasterRecord(string Code, string Name);
internal sealed record ProviderLine(string Id, string ItemCode, string Description, decimal Quantity, decimal InvoiceableQuantity,
    decimal Price, decimal Discount, string? TaxCode);
internal sealed record ProviderOrder(string Id, string Reference, DateOnly OrderedOn, string Currency, string Status,
    string CustomerCode, string CustomerName, ProviderLine[] Lines, decimal Shipping = 0, decimal Refund = 0, bool Cancelled = false, int Revision = 1);

internal interface IConnectorAdapter
{
    string Key { get; }
    string[] Capabilities { get; }
    ProviderAccount Discover();
    string? Test(string protectedProof, string scenario);
    ProviderField[] Fields { get; }
    ProviderMasterRecord[] ReadMasterData(string capability);
    ProviderOrder[] ReadOrders(string resource, string? search);
    Task SubmitAsync(string operation, CancellationToken ct) => Task.FromException(new NotSupportedException("External document submission requires an approved runtime adapter."));
    Task<ProviderOrder?> ReadDocumentAsync(string reference, CancellationToken ct) => Task.FromException<ProviderOrder?>(new NotSupportedException("External committed documents require an approved runtime adapter."));
}

internal sealed class DevelopmentAdapters(IWebHostEnvironment environment)
{
    public IConnectorAdapter? Get(string key) => !environment.IsDevelopment() ? null : key switch
    {
        "mock-accounting" => Accounting,
        "mock-commerce" => Commerce,
        _ => null
    };

    private static readonly MockProvider Accounting = new(false);
    private static readonly MockProvider Commerce = new(true);

    private sealed class MockProvider(bool commerce) : IConnectorAdapter
    {
        public string Key => commerce ? "mock-commerce" : "mock-accounting";
        public string[] Capabilities => ConnectorRules.MockCapabilities(Key);
        public ProviderAccount Discover() => commerce
            ? new("sample-merchant", "Development merchant", [new("sample-store-mnl", "Demo Manila Store", "Store"), new("sample-store-ceb", "Demo Cebu Store", "Store")])
            : new("sample-accountant", "Development accounting account", [new("sample-company-mnl", "Demo Manila Company File", "CompanyFile"), new("sample-company-ceb", "Demo Cebu Company File", "CompanyFile")]);

        public string? Test(string protectedProof, string scenario) => !Guid.TryParse(protectedProof, out _) || scenario == "authentication"
            ? "AuthenticationFailed" : scenario == "timeout" ? "TimedOut" : scenario == "capability" ? "CapabilityUnavailable" : null;

        public ProviderField[] Fields =>
        [
            new("Header", "reference", "string", true), new("Header", "customerCode", "string", true, ["DEMO-BUYER"]),
            new("Header", "orderedOn", "date", true), new("Header", "currency", "string", true, ["PHP"]),
            new("Header", "status", "string", true, ["Open", "Paid", "Cancelled", "Partially Refunded"]),
            new("Line", "itemCode", "string", true, ["DEMO-SERVICE"]), new("Line", "quantity", "decimal", true),
            new("Line", "price", "decimal", true), new("Line", "taxCode", "string", false, ["VAT12"]),
            new("Line", "discount", "decimal", false), new("Header", "shipping", "decimal", false)
        ];

        public ProviderMasterRecord[] ReadMasterData(string capability) => capability switch
        {
            "CustomerRead" => [new("DEMO-BUYER", "Development Buyer")],
            "ItemRead" or "ProductRead" => [new("DEMO-SERVICE", "Development Service")],
            "TaxCodeRead" => [new("VAT12", "Development standard VAT code")],
            "UnitOfMeasureRead" => [new("EA", "Each")],
            "AccountingDimensionRead" => [new("MNL", "Development Manila Department"), new("PRJ-1", "Development Pilot Project")],
            "DiscountRead" => [new("PROMO5", "Development order discount")],
            _ => []
        };

        public ProviderOrder[] ReadOrders(string resource, string? search)
        {
            if (!Discover().Resources.Any(x => x.Id == resource)) return [];
            var date = new DateOnly(2026, 1, 1);
            var prefix = commerce ? "SHOP-" : "EXT-SO-";
            var region = resource.EndsWith("-cebu", StringComparison.Ordinal) || resource.EndsWith("-ceb", StringComparison.Ordinal) ? "CEB-" : "MNL-";
            ProviderOrder[] rows =
            [
                new("0001", prefix + region + "1001", date, "PHP", commerce ? "Paid" : "Open", "DEMO-BUYER", "Development Buyer",
                    [new("001", "DEMO-SERVICE", "Development Service", 5, 5, 100, commerce ? 5 : 0, "VAT12")], commerce ? 25 : 0),
                new("0002", prefix + region + "1002", date, "PHP", "Cancelled", "DEMO-BUYER", "Development Buyer",
                    [new("001", "DEMO-SERVICE", "Cancelled Development Service", 1, 0, 100, 0, "VAT12")], Refund: commerce ? 100 : 0, Cancelled: true),
                new("0003", prefix + region + "1003", date, "PHP", commerce ? "Partially Refunded" : "Open", "DEMO-BUYER", "Development Buyer",
                    [new("001", "DEMO-SERVICE", "Development Service", 3, 2, 100, 0, "VAT12")], Refund: commerce ? 100 : 0)
            ];
            return rows.Where(x => search is null || x.Reference.Contains(search, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
    }
}

