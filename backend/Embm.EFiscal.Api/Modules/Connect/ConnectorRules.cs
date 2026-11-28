using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record Capability(string Code, string Verification, string? Documentation);
internal sealed record ManualOperation(string Method, string Path);
internal sealed record ConnectorConfig(string Name, string Description, string Deployment, string ApiVersion,
    string[] Editions, string[] Regions, string[] Documentation, string[] Requirements, string[] Authentication,
    string Priority, string? LogoLabel, Capability[] Capabilities, string[] Origins, ManualOperation[] ManualOperations);
internal sealed record ConnectorDraft(string? Code, string Category, string Adapter, ConnectorConfig Configuration, int ExpectedRevision = 0);

internal static class ConnectorRules
{
    public const string View = "Connectors.View";
    public const string Edit = "Connectors.Edit";
    public const string Publish = "Connectors.Publish";
    public static readonly string[] VendorGrants = [View, Edit, Publish];
    public static readonly string[] Categories = ["Accounting Software", "E-Commerce", "ERP", "POS", "Custom Business Software"];
    public static readonly string[] Deployments = ["Configurable REST API", "Dedicated Cloud Adapter", "Desktop Agent Adapter", "QuickBooks Web Connector", "Other Approved Adapter"];
    public static readonly string[] Authentication = ["API Key", "Bearer Token", "Basic TLS", "OAuth Authorization Code", "OAuth Client Credentials",
        "OAuth Refresh", "Reviewed Request Signing", "Desktop Agent Authorization", "QuickBooks Web Connector Authorization", "Synthetic Authorization"];
    public static readonly string[] Accounting = ["DiscoverCompanyFiles", "CustomerRead", "ItemRead", "TaxCodeRead", "UnitOfMeasureRead", "AccountingDimensionRead",
        "SalesOrderRead", "SalesOrderLineRead", "SalesOrderCreate", "SalesInvoiceCreate", "CreditMemoCreate", "InvoiceOrderLink", "PartialInvoicing",
        "CustomHeaderFields", "CustomLineFields", "InvoiceRead", "CreditMemoRead", "ExternalDocumentStatusRead", "Reconciliation"];
    public static readonly string[] Commerce = ["DiscoverShops", "OrderRead", "OrderLineRead", "ProductRead", "CustomerRead", "DiscountRead", "ShippingChargeRead",
        "TaxRead", "PaymentStatusRead", "FulfillmentStatusRead", "RefundRead", "CancellationRead", "WebhookSupport", "InvoiceReferenceWrite"];
    public static readonly string[] AccountingMock = ["DiscoverCompanyFiles", "CustomerRead", "ItemRead", "TaxCodeRead", "UnitOfMeasureRead",
        "AccountingDimensionRead", "SalesOrderRead", "SalesOrderLineRead", "InvoiceOrderLink", "PartialInvoicing",
        "SalesInvoiceCreate", "CreditMemoCreate", "SalesOrderCreate", "InvoiceRead", "CreditMemoRead", "ExternalDocumentStatusRead"];
    public static readonly string[] CommerceMock = ["DiscoverShops", "OrderRead", "OrderLineRead", "ProductRead", "CustomerRead", "DiscountRead",
        "ShippingChargeRead", "TaxRead", "PaymentStatusRead", "FulfillmentStatusRead", "RefundRead", "CancellationRead"];

    public static bool IsMock(string adapter) => adapter is "mock-accounting" or "mock-commerce";
    public static string[] MockCapabilities(string adapter) => adapter == "mock-accounting" ? AccountingMock : adapter == "mock-commerce" ? CommerceMock : [];
    public static ConnectorConfig Read(ConnectorVersion version) => version.Configuration.Deserialize<ConnectorConfig>(SpecImport.Json)!;
    public static JsonElement Freeze<T>(T value) => JsonSerializer.SerializeToElement(value, SpecImport.Json);
    public static void Require(bool allowed, string message)
    {
        if (!allowed) throw new ConnectorInputError(message);
    }

    public static string Code(string? raw)
    {
        var code = SpecImport.Text(raw, 55).ToLowerInvariant();
        Require(code.Length >= 3 && code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && char.IsAsciiLetter(code[0]), "Enter a connector code of at least three lowercase letters, digits or hyphens.");
        return code;
    }

    public static ConnectorConfig Normalize(ConnectorDraft input)
    {
        Require(Categories.Contains(input.Category), "Choose a supported connector category.");
        Require(input.Adapter == "review-only" || input.Category == "Accounting Software" && input.Adapter == "mock-accounting" ||
            input.Category == "E-Commerce" && input.Adapter == "mock-commerce", "Choose an adapter that matches the category.");
        var c = input.Configuration ?? throw new ConnectorInputError("Supply a connector configuration.");
        var name = SpecImport.Text(c.Name, 100);
        var description = SpecImport.Text(c.Description, 500);
        Require(name != "" && description != "" && Deployments.Contains(c.Deployment) && c.Priority is "Primary" or "Planned" or "Additional",
            "Supply the name, description, approved deployment type and priority.");
        Require((c.Capabilities?.Length ?? 0) <= 40 && (c.ManualOperations?.Length ?? 0) <= 150, "The connector configuration exceeds its size limits.");
        var permitted = input.Category == "Accounting Software" ? Accounting : input.Category == "E-Commerce" ? Commerce : Accounting.Concat(Commerce).ToArray();
        var caps = (c.Capabilities ?? []).Select(cap =>
        {
            Require(cap is not null && permitted.Contains(cap.Code) &&
                (cap.Verification == "Proposed" || cap.Verification == "Development Verified" && MockCapabilities(input.Adapter).Contains(cap.Code)),
                "A capability or its verification level is unavailable for this adapter.");
            return cap! with { Documentation = Blank(cap!.Documentation) ? null : SpecImport.Https(cap.Documentation, true) };
        }).ToArray();
        Require(caps.Select(x => x.Code).Distinct().Count() == caps.Length, "Capability codes must be unique.");
        var auth = List(c.Authentication, 12, 55);
        Require(auth.All(x => Authentication.Contains(x) && (x != "Synthetic Authorization" || IsMock(input.Adapter))), "Choose supported authentication definitions.");
        var operations = (c.ManualOperations ?? []).Select(x => x is null ? throw new ConnectorInputError("Supply an operation method and path.") :
            new ManualOperation(SpecImport.Method(x.Method), SpecImport.Path(x.Path))).ToArray();
        Require(operations.Distinct().Count() == operations.Length, "Manual operations must be unique.");
        return new(name, description, c.Deployment, SpecImport.Text(c.ApiVersion, 42), List(c.Editions, 15, 80), List(c.Regions, 16, 45),
            List(c.Documentation, 10, 340).Select(x => SpecImport.Https(x, true)).ToArray(), List(c.Requirements, 18, 180), auth,
            c.Priority, SpecImport.Text(c.LogoLabel, 5), caps, List(c.Origins, 12, 340).Select(x => SpecImport.Https(x)).ToArray(), operations);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
    private static string[] List(string[]? values, int count, int length)
    {
        Require((values?.Length ?? 0) <= count, "The connector configuration contains too many values.");
        return (values ?? []).Select(x => SpecImport.Text(x, length)).Where(x => x != "").Distinct().ToArray();
    }

    public static async Task GrantLocal(EfiscalDbContext db, Guid user)
    {
        var current = await db.VendorConnectorGrants.Where(x => x.UserId == user).Select(x => x.Permission).ToListAsync();
        db.VendorConnectorGrants.AddRange(VendorGrants.Concat(Reporting.VendorAccreditation.Grants).Append(Reporting.VendorRegulatoryReview.Manage).Concat(Reporting.RegulatoryConnector.Grants).Except(current)
            .Select(permission => new VendorConnectorGrant { UserId = user, Permission = permission }));
    }
}

