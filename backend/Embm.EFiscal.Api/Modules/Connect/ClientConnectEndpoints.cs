using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed record BindConnection(Guid VersionId, string? Proof, string? ResourceId, string? Name, Guid? BranchId);
internal sealed record MappingInput(Guid CustomerId, Guid ItemId, Guid UnitId, int ExpectedVersion);
internal sealed record TestConnection(string? Scenario);
internal sealed record ReauthorizeConnection(string? Proof);

internal static partial class ClientConnectEndpoints
{
    public const string Manage = "Integration.Manage";
    private const string Mapping = "Mapping.Manage";

    public static void MapClientConnect(this WebApplication app)
    {
        var connectors = app.MapGroup("/api/workspace/{subscriber:guid}/companies/{company:guid}/connect")
            .RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect).WithTags("EMBM eFiscal Connect");
        connectors.MapGet("/catalog", Catalog).AddEndpointFilter(Access(Permissions.CompanyView));
        connectors.MapGet("/catalog/{code}", Detail).AddEndpointFilter(Access(Permissions.CompanyView));
        connectors.MapGet("/connections", Connections).AddEndpointFilter(Access(Permissions.CompanyView));
        connectors.MapGet("/history", History).AddEndpointFilter(Access(Manage));
        connectors.MapGet("/source-orders", SourceOrders).AddEndpointFilter(Access(Manage));
        connectors.MapGet("/source-orders/{id:guid}", SourceOrderDetail).AddEndpointFilter(Access(Manage));
        if (!app.Environment.IsDevelopment()) return;
        MapBridge(connectors);
        MapFieldMapping(connectors);
        MapAccounting(connectors);
        MapReconciliation(connectors);
        connectors.MapPost("/catalog/{code}/authorize", Authorize).AddEndpointFilter(Access(Manage)).RequireRateLimiting("connector-operation");
        connectors.MapPost("/connections", Bind).AddEndpointFilter(Access(Manage, true));
        connectors.MapPost("/connections/{id:guid}/test", Test).AddEndpointFilter(Access(Manage, true)).RequireRateLimiting("connector-operation");
        connectors.MapPut("/connections/{id:guid}/mapping", ConfigureMapping).AddEndpointFilter(Access(Mapping, true));
        connectors.MapGet("/connections/{id:guid}/mapping", ViewMapping).AddEndpointFilter(Access(Mapping));
        connectors.MapPost("/connections/{id:guid}/activate", Activate).AddEndpointFilter(Access(Manage, true));
        connectors.MapPost("/connections/{id:guid}/disconnect", Disconnect).AddEndpointFilter(Access(Manage, true));
        connectors.MapPost("/connections/{id:guid}/reauthorize", Reauthorize).AddEndpointFilter(Access(Manage, true));
        connectors.MapGet("/connections/{id:guid}/master-data/{capability}", Master).AddEndpointFilter(Access(Manage));
        connectors.MapGet("/connections/{id:guid}/sales-orders", SalesOrders).AddEndpointFilter(Access(Manage));
        connectors.MapPost("/connections/{id:guid}/import-orders", ImportOrders).AddEndpointFilter(Access(Manage, true)).RequireRateLimiting("connector-operation");
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Access(string permission, bool write = false) =>
        async (context, next) =>
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        var subscriber = Guid.Parse(http.Request.RouteValues["subscriber"]!.ToString()!);
        var company = Guid.Parse(http.Request.RouteValues["company"]!.ToString()!);
        var ct = http.RequestAborted;
        try
        {
            if (!write)
            {
                if (await CompanyAccess(db, http, subscriber, company, permission, ct) is null) return TenantAccess.Missing();
                return await next(context);
            }
            if (await CompanyAccess(db, http, subscriber, company, permission, ct) is null) return TenantAccess.Missing();
            await using var tx = await db.LockSubscriberAsync(subscriber, ct);
            if (await CompanyAccess(db, http, subscriber, company, permission, ct) is null) return TenantAccess.Missing();
            var result = await next(context);
            if (result is IStatusCodeHttpResult { StatusCode: >= 400 }) return result;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        }
        catch (ConnectorInputError error) { return ApiSupport.Invalid("connection", error.Message); }
    };

    private static async Task<TenantGrant?> CompanyAccess(EfiscalDbContext db, HttpContext http, Guid subscriber, Guid company, string permission, CancellationToken ct)
    {
        var tenant = await TenantAccess.ResolveAsync(db, http, subscriber, permission, ct);
        if (tenant is null || !await TenantAccess.Companies(db, subscriber, http.UserId(), tenant).AnyAsync(x => x.Id == company, ct)) return null;
        http.Items[typeof(TenantGrant)] = tenant;
        return tenant;
    }
    private static TenantGrant Grant(HttpContext http) => (TenantGrant)http.Items[typeof(TenantGrant)]!;
    private static bool MockAccess(HttpContext http) => Grant(http).Subscriber.DevelopmentFixture &&
        http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();

    private static async Task<IResult> Catalog(Guid subscriber, HttpContext http, ConnectionRuntime runtime, CancellationToken ct)
    {
        var plan = await runtime.Plan(subscriber, ct);
        var rows = await runtime.Catalog().AsNoTracking().Where(x => MockAccess(http) || !x.Adapter.StartsWith("mock-"))
            .OrderBy(x => x.Category).ThenBy(x => x.Connector.Code).Take(250).ToArrayAsync(ct);
        List<object> items = [];
        foreach (var row in rows) items.Add(await CatalogCard(row, Grant(http), runtime, plan, ct));
        return Results.Ok(new { items, connectorLimit = plan?.ConnectorLimit ?? 0 });
    }

    private static async Task<IResult> Detail(Guid subscriber, string code, HttpContext http, ConnectionRuntime runtime, CancellationToken ct)
    {
        var version = await runtime.Catalog().AsNoTracking().SingleOrDefaultAsync(x => x.Connector.Code == code, ct);
        if (version is null || IsMock(version.Adapter) && !MockAccess(http)) return TenantAccess.Missing();
        return Results.Ok(await CatalogCard(version, Grant(http), runtime, await runtime.Plan(subscriber, ct), ct));
    }

    private static async Task<object> CatalogCard(ConnectorVersion v, TenantGrant grant, ConnectionRuntime runtime,
        Modules.Billing.PlanVersion? plan, CancellationToken ct)
    {
        var cfg = Read(v);
        return new { versionId = v.Id, connectorId = v.ConnectorId, v.Connector.Code, version = v.Number, v.Category,
            availability = v.Connector.Lifecycle == "Retired" ? "Retired" : v.Connector.Disabled ? "Temporarily Unavailable" : v.Connector.Lifecycle ?? v.Availability,
            cfg.Name, cfg.Description, cfg.LogoLabel, cfg.Deployment, cfg.Editions, cfg.Regions, cfg.ApiVersion, cfg.Documentation,
            cfg.Authentication, cfg.Requirements, cfg.Capabilities, cfg.Priority, development = IsMock(v.Adapter),
            canConnect = v.Connector.Lifecycle is null && grant.Grants.Contains(Manage) && await runtime.Allows(grant, v, plan, ct) };
    }

    private static async Task<IResult> Authorize(Guid subscriber, Guid company, string code, Guid? connection, HttpContext http, ConnectionRuntime runtime,
        DevelopmentAdapters adapters, CancellationToken ct)
    {
        var version = connection is Guid pinned
            ? (await Find(runtime, subscriber, company, pinned, ct))?.Version
            : await runtime.Catalog().AsNoTracking().SingleOrDefaultAsync(x => x.Connector.Code == code, ct);
        if (version is null || version.Connector.Code != code || (connection is null && version.Connector.Lifecycle is not null) || !await runtime.Allows(Grant(http), version, await runtime.Plan(subscriber, ct), ct) ||
            adapters.Get(version.Adapter) is not { } adapter) return TenantAccess.Missing();
        var capability = version.Category == "E-Commerce" ? "DiscoverShops" : "DiscoverCompanyFiles";
        if (!ConnectionRuntime.Capable(version, adapter, capability)) return MissingCapability();
        var account = adapter.Discover();
        var proof = runtime.Authorize(new(subscriber, company, http.UserId(), version.Id, account.Id));
        return Results.Ok(new { account, proof, versionId = version.Id, development = true });
    }

    private static async Task<IResult> Bind(Guid subscriber, Guid company, BindConnection input, HttpContext http, EfiscalDbContext db,
        ConnectionRuntime runtime, DevelopmentAdapters adapters, CancellationToken ct)
    {
        var ticket = runtime.ReadTicket(input.Proof);
        if (ticket is null || ticket.Subscriber != subscriber || ticket.Company != company || ticket.User != http.UserId() || ticket.Version != input.VersionId)
            return TenantAccess.Missing();
        var version = await runtime.Catalog().AsNoTracking().SingleOrDefaultAsync(x => x.Id == input.VersionId, ct);
        var plan = await runtime.Plan(subscriber, ct);
        if (version is null || version.Connector.Lifecycle is not null || !await runtime.Allows(Grant(http), version, plan, ct) || adapters.Get(version.Adapter) is not { } adapter)
            return TenantAccess.Missing();
        var account = adapter.Discover();
        var resource = account.Resources.SingleOrDefault(x => x.Id == input.ResourceId);
        if (resource is null || account.Id != ticket.Account) return TenantAccess.Missing();
        if (await runtime.Scoped(subscriber, company).AnyAsync(x => x.ExternalAccountId == account.Id && x.ExternalResourceId == resource.Id, ct))
            return Conflict("This account resource already has a binding for this company.");
        if (await db.ExternalConnections.CountAsync(x => x.SubscriberId == subscriber && x.Status != "Disconnected", ct) >= plan!.ConnectorLimit)
            return Conflict("The subscriber’s connection allowance has been reached.");
        if (input.BranchId.HasValue && !await db.Branches.AnyAsync(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Id == input.BranchId, ct))
            return TenantAccess.Missing();
        var name = SpecImport.Text(input.Name, 100);
        Require(name.Length > 0, "Enter a connection name.");
        var link = new ExternalConnection { SubscriberId = subscriber, CompanyId = company, BranchId = input.BranchId, Name = name,
            ConnectorId = version.ConnectorId, ConnectorVersionId = version.Id, ExternalAccountId = account.Id,
            ExternalResourceId = resource.Id, ExternalResourceName = resource.Name, ResourceType = resource.Type, CreatedBy = http.UserId() };
        var credential = new ConnectionCredential { Id = link.CredentialReference, SubscriberId = subscriber, CompanyId = company };
        runtime.Rotate(link, credential);
        db.AddRange(credential, link);
        Audit(db, http, link, "Bound");
        return Results.Created("/api/workspace/" + subscriber + "/companies/" + company + "/connect/connections", Present(link, version));
    }

    private static async Task<IResult> Connections(Guid subscriber, Guid company, HttpContext http, ConnectionRuntime runtime, CancellationToken ct)
    {
        var rows = await runtime.Scoped(subscriber, company).AsNoTracking().Include(x => x.Version).ThenInclude(x => x.Connector)
            .Where(x => MockAccess(http) || !x.Version.Adapter.StartsWith("mock-")).OrderByDescending(x => x.CreatedAt).Take(250).ToArrayAsync(ct);
        return Results.Ok(new { items = rows.Select(x => Present(x, x.Version)), canManage = Grant(http).Grants.Contains(Manage) });
    }

    private static Task<ExternalConnection?> Find(ConnectionRuntime runtime, Guid subscriber, Guid company, Guid id, CancellationToken ct) =>
        runtime.Scoped(subscriber, company).Include(x => x.Version).ThenInclude(x => x.Connector).SingleOrDefaultAsync(x => x.Id == id, ct);

    private static async Task<IResult> Test(Guid subscriber, Guid company, Guid id, TestConnection input, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, DevelopmentAdapters adapters, CancellationToken ct)
    {
        var scenario = input.Scenario ?? "success";
        Require(scenario is "success" or "authentication" or "timeout" or "capability", "Choose a supported development test scenario.");
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !await runtime.Allows(Grant(http), link.Version, await runtime.Plan(subscriber, ct), ct) ||
            adapters.Get(link.Version.Adapter) is not { } adapter) return TenantAccess.Missing();
        if (link.Authorization != "Authorized" || await runtime.Proof(link, ct) is not { } proof)
            return Conflict("Authorize this connection again before testing.");
        var failure = adapter.Test(proof, scenario);
        link.LastError = failure;
        if (failure is null)
        {
            link.LastSuccessfulTest = DateTimeOffset.UtcNow;
            if (link.Status != "Connected") link.Status = link.MappingVersion is null ? "Mapping Required" : "Configuration Required";
        }
        else
        {
            link.Status = failure == "AuthenticationFailed" ? "Authentication Expired" : "Connection Failed";
            link.LastSuccessfulTest = null;
            if (failure == "AuthenticationFailed") link.Authorization = "Expired";
        }
        Audit(db, http, link, failure is null ? "TestSucceeded" : "Test." + failure);
        return Results.Ok(Present(link, link.Version));
    }

    private static async Task<IResult> ViewMapping(Guid subscriber, Guid company, Guid id, ConnectionRuntime runtime,
        DevelopmentAdapters adapters, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !MockAccess(http) || adapters.Get(link.Version.Adapter) is not { } adapter)
            return TenantAccess.Missing();
        var prior = await db.ConnectionMappings.AsNoTracking().Where(x => x.ConnectionId == id && x.SubscriberId == subscriber && x.CompanyId == company)
            .OrderByDescending(x => x.Number).Take(25).Select(x => new { x.Number, x.CustomerId, x.ItemId, x.UnitId, x.CreatedAt }).ToArrayAsync(ct);
        return Results.Ok(new { currentVersion = link.MappingVersion, template = "Synthetic source order v1", fields = adapter.Fields, versions = prior });
    }

    private static async Task<IResult> ConfigureMapping(Guid subscriber, Guid company, Guid id, MappingInput input, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, DevelopmentAdapters adapters, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !await runtime.Allows(Grant(http), link.Version, await runtime.Plan(subscriber, ct), ct) ||
            adapters.Get(link.Version.Adapter) is not { } adapter) return TenantAccess.Missing();
        if (link.Authorization != "Authorized") return Conflict("Authorize the connection before saving mapping.");
        if ((link.MappingVersion ?? 0) != input.ExpectedVersion) return Conflict("Reload the current mapping before publishing another version.");
        var master = db.MasterRecords.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.Active);
        var expected = new[] { (input.CustomerId, "customers"), (input.ItemId, "items"), (input.UnitId, "units") };
        foreach (var (target, kind) in expected)
            if (!await master.AnyAsync(x => x.Id == target && x.Kind == kind, ct)) return TenantAccess.Missing();
        var item = await master.SingleAsync(x => x.Id == input.ItemId, ct);
        if (item.Data.Ref("unit") != input.UnitId)
            return ApiSupport.Invalid("unitId", "Use the company item’s base unit for the synthetic order map.");
        var mapping = new ConnectionMapping { SubscriberId = subscriber, CompanyId = company, ConnectionId = id,
            Number = input.ExpectedVersion + 1, CustomerId = input.CustomerId, ItemId = input.ItemId, UnitId = input.UnitId,
            FieldMetadata = Freeze(adapter.Fields), CreatedBy = http.UserId() };
        db.ConnectionMappings.Add(mapping);
        link.MappingVersion = mapping.Number;
        link.MappingStatus = "Configured";
        link.Status = "Configuration Required";
        Audit(db, http, link, "MappingVersionCreated");
        return Results.Ok(Present(link, link.Version));
    }

    private static async Task<IResult> Activate(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db,
        ConnectionRuntime runtime, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        var plan = await runtime.Plan(subscriber, ct);
        if (link is null || !await runtime.Allows(Grant(http), link.Version, plan, ct)) return TenantAccess.Missing();
        if (link.Authorization != "Authorized" || link.MappingVersion is null || link.LastSuccessfulTest is null || link.LastError is not null ||
            await runtime.Proof(link, ct) is null)
            return Conflict("A successful connection test and a company mapping are required to activate.");
        if (await db.ExternalConnections.CountAsync(x => x.SubscriberId == subscriber && x.Id != id && x.Status != "Disconnected", ct) >= plan!.ConnectorLimit)
            return Conflict("The subscriber’s connection allowance has been reached.");
        link.Status = "Connected";
        Audit(db, http, link, "Activated");
        return Results.Ok(Present(link, link.Version));
    }

    private static async Task<IResult> Disconnect(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db,
        ConnectionRuntime runtime, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        if (link is null || !MockAccess(http)) return TenantAccess.Missing();
        var credential = await db.ConnectionCredentials.FindAsync([link.CredentialReference], ct);
        if (credential is not null)
        {
            credential.ProtectedValue = "";
            credential.RevokedAt = DateTimeOffset.UtcNow;
        }
        link.Authorization = "Revoked";
        link.Status = "Disconnected";
        link.LastSuccessfulTest = null;
        Audit(db, http, link, "Disconnected");
        return Results.Ok(Present(link, link.Version));
    }

    private static async Task<IResult> Reauthorize(Guid subscriber, Guid company, Guid id, ReauthorizeConnection input, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, DevelopmentAdapters adapters, CancellationToken ct)
    {
        var link = await Find(runtime, subscriber, company, id, ct);
        var plan = await runtime.Plan(subscriber, ct);
        var ticket = runtime.ReadTicket(input.Proof);
        if (link is null || ticket is null || ticket.Subscriber != subscriber || ticket.Company != company || ticket.User != http.UserId() ||
            ticket.Account != link.ExternalAccountId || ticket.Version != link.ConnectorVersionId || !await runtime.Allows(Grant(http), link.Version, plan, ct)
            || adapters.Get(link.Version.Adapter) is null) return TenantAccess.Missing();
        if (await db.ExternalConnections.CountAsync(x => x.SubscriberId == subscriber && x.Id != id && x.Status != "Disconnected", ct) >= plan!.ConnectorLimit)
            return Conflict("The subscriber’s connection allowance has been reached.");
        var credential = (await db.ConnectionCredentials.FindAsync([link.CredentialReference], ct))!;
        runtime.Rotate(link, credential);
        credential.Revision++;
        link.Authorization = "Authorized";
        link.Status = "Configuration Required";
        link.LastSuccessfulTest = null;
        link.LastError = null;
        Audit(db, http, link, "CredentialsRotated");
        return Results.Ok(Present(link, link.Version));
    }

    private static async Task<IResult> Master(Guid subscriber, Guid company, Guid id, string capability, ConnectionRuntime runtime, CancellationToken ct)
    {
        if (capability is not ("CustomerRead" or "ItemRead" or "ProductRead" or "TaxCodeRead" or "UnitOfMeasureRead" or "AccountingDimensionRead" or "DiscountRead"))
            return MissingCapability();
        var provider = await runtime.Active(subscriber, company, id, [capability], false, ct);
        return provider is null ? TenantAccess.Missing() : Results.Ok(new { items = provider.Adapter.ReadMasterData(capability) });
    }

    private static async Task<IResult> SalesOrders(Guid subscriber, Guid company, Guid id, string? q, ConnectionRuntime runtime, CancellationToken ct)
    {
        var provider = await runtime.Active(subscriber, company, id, ["SalesOrderRead", "SalesOrderLineRead"], true, ct);
        if (provider is null) return TenantAccess.Missing();
        var search = SpecImport.Text(q, 70);
        var ableToLink = ConnectionRuntime.Capable(provider.Connection.Version, provider.Adapter, "InvoiceOrderLink");
        return Results.Ok(new { items = provider.Adapter.ReadOrders(provider.Connection.ExternalResourceId, search).Select(order =>
            new { order, key = ableToLink && Eligible(order) ? ConnectionRuntime.SalesKey(id, order.Id) : null }) });
    }

    private static async Task<IResult> ImportOrders(Guid subscriber, Guid company, Guid id, HttpContext http,
        EfiscalDbContext db, ConnectionRuntime runtime, string? after, DateTimeOffset? changedSince, string? scenario, int? limit, CancellationToken ct)
    {
        var provider = await runtime.Active(subscriber, company, id, ["OrderRead", "OrderLineRead"], true, ct);
        if (provider is null || provider.Connection.ResourceType != "Store") return TenantAccess.Missing();
        var size = Math.Clamp(limit ?? 25, 1, 25);
        var records = SourceObservations.Page(provider.Adapter, provider.Connection.ExternalResourceId, after, changedSince, scenario, size);
        var current = records.Take(size).ToArray();
        var keys = current.Select(x => x.Id).ToArray();
        var existing = await db.SourceOrders.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company &&
            x.ConnectionId == id && keys.Contains(x.ExternalOrderId)).ToDictionaryAsync(x => x.ExternalOrderId, ct);
        var latest = await SourceObservations.Latest(db, subscriber, company, existing.Values.Select(x => x.Id).ToArray(), ct);
        var imported = 0;
        var updated = 0;
        foreach (var source in current)
        {
            if (existing.TryGetValue(source.Id, out var prior))
            {
                var old = SourceObservations.Current(prior, latest.GetValueOrDefault(prior.Id));
                if (old is null || source.Revision <= old.Revision) continue;
                db.SourceOrderObservations.Add(new SourceOrderObservation { SubscriberId = subscriber, CompanyId = company, SourceOrderId = prior.Id,
                    Revision = source.Revision, Snapshot = Freeze(source) });
                updated++;
            }
            else
            {
                db.SourceOrders.Add(new SourceOrder { SubscriberId = subscriber, CompanyId = company, ConnectionId = id, ExternalOrderId = source.Id,
                    Reference = source.Reference, Date = source.OrderedOn, Status = source.Status, Currency = source.Currency, Data = Freeze(source) });
                imported++;
            }
        }
        Audit(db, http, provider.Connection, "SourceOrdersRetrieved");
        return Results.Ok(new { imported, updated, alreadyPresent = current.Length - imported - updated,
            nextCursor = records.Length > size ? current[^1].Id : null });
    }

    private static IQueryable<SourceOrder> Sources(EfiscalDbContext db, Guid subscriber, Guid company) =>
        db.SourceOrders.AsNoTracking().Include(x => x.Connection).ThenInclude(x => x.Version).ThenInclude(x => x.Connector)
            .Where(x => x.SubscriberId == subscriber && x.CompanyId == company);

    private static async Task<IResult> SourceOrders(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return Results.Ok(new { items = Array.Empty<object>() });
        var orders = await Sources(db, subscriber, company).OrderByDescending(x => x.RetrievedAt).ThenBy(x => x.Reference).Take(150).ToArrayAsync(ct);
        var latest = await SourceObservations.Latest(db, subscriber, company, orders.Select(x => x.Id).ToArray(), ct);
        return Results.Ok(new { items = orders.Select(x => SourceView(x, latest.GetValueOrDefault(x.Id))) });
    }

    private static async Task<IResult> SourceOrderDetail(Guid subscriber, Guid company, Guid id, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!MockAccess(http)) return TenantAccess.Missing();
        var source = await Sources(db, subscriber, company).SingleOrDefaultAsync(x => x.Id == id, ct);
        if (source is null) return TenantAccess.Missing();
        var key = ConnectionRuntime.OrderKey(id);
        var links = await db.FiscalDocuments.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && x.ExternalOrderKey == key)
            .OrderBy(x => x.Number).Select(x => new { x.Id, x.Number }).ToArrayAsync(ct);
        var latest = await SourceObservations.Latest(db, subscriber, company, [source.Id], ct);
        return Results.Ok(new { record = SourceView(source, latest.GetValueOrDefault(source.Id)), relatedInvoices = links });
    }

    private static object SourceView(SourceOrder source, SourceOrderObservation? update)
    {
        var normalized = SourceObservations.Current(source, update)!;
        return new { source.Id, source.ConnectionId, provider = Read(source.Connection.Version).Name, shop = source.Connection.ExternalResourceName,
            shopId = source.Connection.ExternalResourceId, source.ExternalOrderId, source.Reference, source.Date, normalized.Status, source.Currency,
            retrievedAt = update?.RetrievedAt ?? source.RetrievedAt, revision = normalized.Revision, order = normalized, key = Eligible(normalized) && source.Connection.Status == "Connected" &&
                !source.Connection.Version.Connector.Disabled ? ConnectionRuntime.OrderKey(source.Id) : null };
    }

    private static async Task<IResult> History(Guid subscriber, Guid company, HttpContext http, EfiscalDbContext db, ConnectionRuntime runtime, CancellationToken ct)
    {
        if (!MockAccess(http)) return Results.Ok(new { items = Array.Empty<object>() });
        var rows = await (from entry in db.SecurityAudits.AsNoTracking()
                          join link in runtime.Scoped(subscriber, company).AsNoTracking() on entry.TargetId equals link.Id
                          where entry.SubscriberId == subscriber && entry.Action.StartsWith("Connections.")
                          orderby entry.Id descending
                          select new { entry.At, entry.Action, entry.ActorId, connection = link.Name, id = link.Id })
            .Take(150).ToArrayAsync(ct);
        return Results.Ok(new { items = rows });
    }

    private static bool Eligible(ProviderOrder order) => !order.Cancelled && order.Lines.Any(x => x.InvoiceableQuantity > 0);
    private static IResult MissingCapability() => ApiSupport.Problem(409, "This capability is unavailable on the active connector version.");
    private static IResult Conflict(string message) => ApiSupport.Problem(409, message);
    private static void Audit(EfiscalDbContext db, HttpContext http, ExternalConnection link, string action) =>
        db.Audit("Connections." + action, http.UserId(), link.SubscriberId, link.Id);

    private static object Present(ExternalConnection link, ConnectorVersion version) => new
    {
        link.Id, link.BranchId, link.Name, link.ExternalAccountId, link.ExternalResourceId, link.ExternalResourceName, link.ResourceType,
        link.ConnectorId, link.ConnectorVersionId, connector = Read(version).Name, connectorCode = version.Connector?.Code, version = version.Number,
        development = IsMock(version.Adapter), status = version.Connector?.Disabled == true && link.Status != "Disconnected" ? "Suspended" : link.Status,
        link.Authorization, link.MappingStatus, link.MappingVersion, link.LastSuccessfulTest, link.LastError, link.CreatedBy, link.CreatedAt
    };
}

