using Embm.EFiscal.Api.Modules.MasterData;
using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record AccountTicket(Guid Subscriber, Guid Company, Guid User, Guid Version, string Account);
internal sealed record AuthorizedProvider(ExternalConnection Connection, IConnectorAdapter Adapter, ConnectionMapping? Mapping);

internal sealed class ConnectionRuntime(EfiscalDbContext db, DevelopmentAdapters adapters, IDataProtectionProvider protection)
{
    public IQueryable<ExternalConnection> Scoped(Guid subscriber, Guid company) =>
        db.ExternalConnections.Where(x => x.SubscriberId == subscriber && x.CompanyId == company);
    public IQueryable<ConnectorVersion> Catalog() =>
        db.ConnectorVersions.Include(x => x.Connector).Where(x => x.State == "Published"
            && !db.ConnectorVersions.Any(v => v.ConnectorId == x.ConnectorId && v.State == "Published" && v.Number > x.Number));

    public async Task<PlanVersion?> Plan(Guid subscriber, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return await (from subscription in db.SubscriberSubscriptions.AsNoTracking()
                      join plan in db.PlanVersions.AsNoTracking() on subscription.PlanVersionId equals plan.Id
                      where subscription.SubscriberId == subscriber && !subscription.Suspended && subscription.PeriodStart <= now && subscription.PeriodEnd > now
                      select plan).SingleOrDefaultAsync(ct);
    }

    public async Task<bool> Allows(TenantGrant tenant, ConnectorVersion version, PlanVersion? plan, CancellationToken ct)
    {
        if (!tenant.Subscriber.DevelopmentFixture || version.Connector.Disabled || version.State != "Published" || version.Availability != "Development Verified" ||
            adapters.Get(version.Adapter) is null || plan is null || plan.ConnectorLimit <= 0) return false;
        var category = version.Category == "Accounting Software" ? "Accounting" : "Commerce";
        if (!plan.ConnectorCategories.Contains(category)) return false;
        return await db.Subscribers.AsNoTracking().AnyAsync(x => x.Id == tenant.Subscriber.Id && x.DevelopmentFixture, ct);
    }

    public string Authorize(AccountTicket ticket) =>
        protection.CreateProtector("EMBM.Connect.MockAuthorization.v1").ToTimeLimitedDataProtector()
            .Protect(System.Text.Json.JsonSerializer.Serialize(ticket), TimeSpan.FromMinutes(10));

    public AccountTicket? ReadTicket(string? value)
    {
        if (value is null || value.Length > 2048) return null;
        try
        {
            var plain = protection.CreateProtector("EMBM.Connect.MockAuthorization.v1").ToTimeLimitedDataProtector().Unprotect(value);
            return System.Text.Json.JsonSerializer.Deserialize<AccountTicket>(plain);
        }
        catch (CryptographicException) { return null; }
    }

    private IDataProtector Credentials(ExternalConnection link) =>
        protection.CreateProtector("EMBM.Connect.CompanyMockCredential.v1", link.SubscriberId.ToString(), link.CompanyId.ToString(),
            link.Id.ToString(), link.ConnectorVersionId.ToString());

    public void Rotate(ExternalConnection link, ConnectionCredential credential)
    {
        credential.ProtectedValue = Credentials(link).Protect(Guid.NewGuid().ToString());
        credential.RevokedAt = null;
    }

    public async Task<string?> Proof(ExternalConnection link, CancellationToken ct)
    {
        var credential = await db.ConnectionCredentials.AsNoTracking().SingleOrDefaultAsync(x => x.Id == link.CredentialReference
            && x.SubscriberId == link.SubscriberId && x.CompanyId == link.CompanyId && x.RevokedAt == null, ct);
        if (credential is null) return null;
        try { return Credentials(link).Unprotect(credential.ProtectedValue); }
        catch (CryptographicException) { return null; }
    }

    public static bool Capable(ConnectorVersion version, IConnectorAdapter adapter, string ability) =>
        adapter.Capabilities.Contains(ability) && Read(version).Capabilities.Any(x => x.Code == ability && x.Verification == "Development Verified");

    public async Task<AuthorizedProvider?> Active(Guid subscriber, Guid company, Guid connection, string[] capabilities, bool requireMapping, CancellationToken ct)
    {
        var link = await Scoped(subscriber, company).AsNoTracking().Include(x => x.Version).ThenInclude(x => x.Connector)
            .SingleOrDefaultAsync(x => x.Id == connection && x.Status == "Connected" && x.Authorization == "Authorized", ct);
        if (link is null || link.Version.Connector.Disabled || link.Version.State != "Published" || adapters.Get(link.Version.Adapter) is not { } adapter ||
            !capabilities.All(x => Capable(link.Version, adapter, x)) || await Proof(link, ct) is null ||
            await Plan(subscriber, ct) is not { } plan || !plan.ConnectorCategories.Contains(link.ResourceType == "Store" ? "Commerce" : "Accounting") ||
            !await db.Subscribers.AnyAsync(x => x.Id == subscriber && x.DevelopmentFixture, ct)) return null;
        var mapping = await db.ConnectionMappings.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.ConnectionId == link.Id && x.Number == link.MappingVersion, ct);
        return mapping is null && requireMapping ? null : new(link, adapter, mapping);
    }

    public static string SalesKey(Guid connection, string order) => "so-" + connection.ToString("N") + "-" + order;
    public static string OrderKey(Guid order) => "shop-" + order.ToString("N");
}

internal sealed class ConnectedSalesOrders(EfiscalDbContext db, ConnectionRuntime runtime, DevelopmentSalesOrders legacy) : IExternalSalesOrderProvider
{
    public async Task<ExternalOrder?> Find(Guid subscriber, Guid company, string key, CancellationToken ct)
    {
        if (key == "sample-order") return await legacy.Find(subscriber, company, key, ct);
        if (key.StartsWith("shop-", StringComparison.Ordinal) && Guid.TryParseExact(key[5..], "N", out var id))
        {
            var record = await db.SourceOrders.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == id, ct);
            if (record is null || await runtime.Active(subscriber, company, record.ConnectionId, ["OrderRead", "OrderLineRead"], true, ct) is not { } provider)
                return null;
            var latest = await SourceObservations.Latest(db, subscriber, company, [record.Id], ct);
            var source = SourceObservations.Current(record, latest.GetValueOrDefault(record.Id));
            return source is null ? null : await AsSource(provider, source, key, ct);
        }
        if (key.Length is > 36 and <= 156 && key.StartsWith("so-", StringComparison.Ordinal) && key[35] == '-' &&
            Guid.TryParseExact(key[3..35], "N", out var connection))
        {
            var provider = await runtime.Active(subscriber, company, connection, ["SalesOrderRead", "SalesOrderLineRead", "InvoiceOrderLink"], true, ct);
            var source = provider?.Adapter.ReadOrders(provider.Connection.ExternalResourceId, null).SingleOrDefault(x => x.Id == key[36..]);
            return source is null ? null : await AsSource(provider!, source, key, ct);
        }
        return null;
    }

    private async Task<ExternalOrder?> AsSource(AuthorizedProvider provider, ProviderOrder order, string key, CancellationToken ct)
    {
        if (order.Cancelled) return null;
        var legacy = provider.Mapping!;
        var link = provider.Connection;
        var profile = await db.MappingProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == link.SubscriberId &&
            x.CompanyId == link.CompanyId && x.ConnectionId == link.Id && x.DocumentType == "SalesInvoice" && x.Direction == FieldMapper.Inbound &&
            x.ActiveVersion != null && !x.Disabled, ct);
        var customer = legacy.CustomerId;
        Guid[] items = order.Lines.Select(_ => legacy.ItemId).ToArray();
        if (profile is not null)
        {
            var version = await ClientConnectEndpoints.Versions(db, profile).AsNoTracking().SingleAsync(x => x.Number == profile.ActiveVersion &&
                x.ConnectorVersionId == link.ConnectorVersionId && x.Validated, ct);
            var references = version.ReferenceSnapshot.Deserialize<BoundReference[]>(SpecImport.Json) ?? [];
            try
            {
                var values = FieldMapper.Map(FieldMapper.Inbound, version.Rules.Deserialize<FieldRule[]>(SpecImport.Json) ?? [],
                    FieldMapper.Order(order), references);
                var authorizedCustomer = references.SingleOrDefault(x => x.Kind == "Customer" && x.ExternalId == order.CustomerCode)?.LocalKey;
                if (!Guid.TryParse(authorizedCustomer, out customer) || !Equals(values.Header.GetValueOrDefault("customer"), authorizedCustomer)) return null;
                items = new Guid[order.Lines.Length];
                for (var i = 0; i < order.Lines.Length; i++)
                {
                    var external = order.Lines[i];
                    var authorizedItem = references.SingleOrDefault(x => x.Kind == "Item" && x.ExternalId == external.ItemCode)?.LocalKey;
                    if (!Guid.TryParse(authorizedItem, out items[i]) || !Equals(values.Lines[i].GetValueOrDefault("item"), authorizedItem)) return null;
                }
            }
            catch (ConnectorInputError) { return null; }
        }
        // Source eligibility, currency, quantities and identities come directly from the provider snapshot.
        var lines = order.Lines.Select((line, i) => new ExternalOrderLine(line.Id, items[i], legacy.UnitId, line.InvoiceableQuantity, 1,
                link.ResourceType == "CompanyFile" ? line.Quantity - line.InvoiceableQuantity : 0))
            .Where(x => x.Quantity > 0).ToArray();
        if (lines.Length == 0) return null;
        var partial = link.ResourceType != "Store" && ConnectionRuntime.Capable(link.Version, provider.Adapter, "PartialInvoicing");
        MarketplaceInvoiceTerms? terms = null;
        var needsReview = false;
        if (link.ResourceType == "Store")
        {
            needsReview = order.Financials is not null || order.Shipping != 0 || order.Refund != 0 || order.Lines.Any(x => x.Discount != 0);
            if (provider.Adapter.Key == "mock-commerce" && legacy.ShippingItemId is Guid shippingId &&
                legacy.SellerDiscountGroupId is Guid discountId && order.Financials is { } source &&
                order.Currency == "PHP" && order.Status == "Paid" && order.Refund == 0 && !order.Cancelled &&
                source is { PlatformFundedVoucher: 0, ShippingSubsidy: 0, Refund: 0, Cancelled: false, CustomerShipping: > 0, SellerFundedDiscount: > 0,
                    Commission: >= 0, ServiceFee: >= 0, ProcessingFee: >= 0, SettlementDeduction: >= 0 } &&
                source.CustomerShipping == order.Shipping && order.Lines is [var product] &&
                product.Quantity == product.InvoiceableQuantity && product.TaxCode == "VAT12" &&
                product.Price * product.Quantity == source.ProductGross && product.Discount == source.SellerFundedDiscount)
            {
                var service = await db.MasterRecords.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == link.SubscriberId &&
                    x.CompanyId == link.CompanyId && x.Id == shippingId && x.Kind == "items" && x.Active, ct);
                var policy = new MarketplaceInvoiceTerms(link.ConnectorVersionId, legacy.Number, order.Revision, source, discountId, product.Id, product.Price);
                if (service?.Data.Str("itemType") == "Services" && service.Data.Ref("unit") is Guid shippingUnit &&
                    policy.InvoiceTotal - source.Commission - source.ServiceFee - source.ProcessingFee - source.SettlementDeduction == source.Settlement)
                {
                    terms = policy;
                    lines = [..lines, new(MarketplaceInvoiceTerms.ShippingLine, shippingId, shippingUnit, 1, 1)];
                    needsReview = false;
                }
            }
        }
        return new(key, order.Reference, Read(link.Version).Name, link.ExternalAccountId, order.Id,
            new DateTimeOffset(order.OrderedOn.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), customer, lines, partial, order.Currency, link.BranchId,
            needsReview ? MarketplaceInvoiceTerms.ReviewRequired : null, link.ExternalResourceId, terms);
    }
}

