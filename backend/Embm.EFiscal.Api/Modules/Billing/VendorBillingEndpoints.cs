using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal record PublishPlan(string Code, string Name, string Description, decimal MonthlyPrice, decimal AnnualPrice,
    int MonthlyCredits, int AnnualCredits, string AllocationFrequency, int CompanyLimit, int UserLimit, int ConnectorLimit,
    string[] Integrations, string[] ConnectorCategories, string[] Features, int? CreditValidityDays, bool Rollover,
    bool RenewalAllowed, bool AutoTopUpAllowed, int GraceDays, bool Active, DateTimeOffset EffectiveAt);
internal record PublishPackage(string Code, string Name, int Credits, decimal Price, int? ValidityDays, string[] EligiblePlans,
    bool Active, DateTimeOffset EffectiveAt);

internal record WalletAdjustment(int Quantity, string Reference, string Reason);

internal static class VendorBillingEndpoints
{
    internal const string ViewCatalog = "Billing.Catalog.View";
    internal const string PublishCatalog = "Billing.Catalog.Publish";
    internal const string ViewMonitor = "Billing.Monitor.View";
    internal const string AdjustCredits = "Billing.Credits.Adjust";
    internal static readonly string[] Grants = [ViewCatalog, PublishCatalog, ViewMonitor, AdjustCredits];

    public static void MapVendorBilling(this WebApplication app)
    {
        var vendor = app.MapGroup("/api/vendor/billing").RequireAuthorization("vendor").AddEndpointFilter(ApiSupport.Protect);
        vendor.MapGet("/access", Access);
        vendor.MapGet("/catalog", Catalog).AddEndpointFilter(Grant(ViewCatalog));
        vendor.MapPost("/plans", Plan).AddEndpointFilter(Grant(PublishCatalog));
        vendor.MapPost("/packages", Package).AddEndpointFilter(Grant(PublishCatalog));
        vendor.MapGet("/monitor", Monitor).AddEndpointFilter(Grant(ViewMonitor));
        vendor.MapPost("/wallets/{subscriber:guid}/adjustments", Adjust).AddEndpointFilter(Grant(AdjustCredits));
    }

    private static Func<EndpointFilterInvocationContext, EndpointFilterDelegate, ValueTask<object?>> Grant(string permission) => async (context, next) =>
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        return await db.VendorConnectorGrants.AnyAsync(x => x.UserId == http.UserId() && x.Permission == permission, http.RequestAborted)
            ? await next(context) : ApiSupport.Problem(403, "Commercial administration permission is required.");
    };

    private static async Task<IResult> Access(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var permissions = await db.VendorConnectorGrants.AsNoTracking().Where(x => x.UserId == http.UserId() && Grants.Contains(x.Permission))
            .Select(x => x.Permission).ToArrayAsync(ct);
        return Results.Ok(new { canViewCatalog = permissions.Contains(ViewCatalog), canPublish = permissions.Contains(PublishCatalog),
            canMonitor = permissions.Contains(ViewMonitor), canAdjust = permissions.Contains(AdjustCredits) });
    }

    private static async Task<IResult> Catalog(EfiscalDbContext db, CancellationToken ct) =>
        Results.Ok(new { plans = await db.PlanVersions.AsNoTracking().OrderBy(x => x.Code).ThenByDescending(x => x.Version).ToArrayAsync(ct),
            packages = await db.PackageVersions.AsNoTracking().OrderBy(x => x.Code).ThenByDescending(x => x.Version).ToArrayAsync(ct) });

    private static async Task<IResult> Plan(PublishPlan input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ValidCode(input.Code) || !ApiSupport.Text(input.Name, 80) || !ApiSupport.Text(input.Description, 500)
            || !BillingEndpoints.ValidMoney(input.MonthlyPrice) || !BillingEndpoints.ValidMoney(input.AnnualPrice)
            || input.MonthlyCredits is < 0 or > 10_000_000 || input.AnnualCredits is < 0 or > 10_000_000
            || input.AllocationFrequency is not ("monthly" or "period") || input.CompanyLimit is < 1 or > 10_000
            || input.UserLimit is < 1 or > 100_000 || input.ConnectorLimit is < 0 or > 10_000
            || !ValidArray(input.Integrations) || !ValidArray(input.ConnectorCategories) || !ValidArray(input.Features)
            || input.CreditValidityDays is < 1 or > 3650 || input.GraceDays is < 0 or > 365 || !ValidEffective(input.EffectiveAt))
            return ApiSupport.Invalid("plan", "Check the plan values and effective date.");
        var previous = await db.PlanVersions.Where(x => x.Code == input.Code).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        var effective = input.EffectiveAt.ToUniversalTime();
        if (previous is not null && effective <= previous.EffectiveAt) return ApiSupport.Invalid("effectiveAt", "Choose a date after the latest version.");
        var plan = new PlanVersion { Code = input.Code, Version = (previous?.Version ?? 0) + 1, Name = input.Name.Trim(),
            Description = input.Description.Trim(), MonthlyPrice = input.MonthlyPrice, AnnualPrice = input.AnnualPrice,
            MonthlyCredits = input.MonthlyCredits, AnnualCredits = input.AnnualCredits, AllocationFrequency = input.AllocationFrequency,
            CompanyLimit = input.CompanyLimit, UserLimit = input.UserLimit, ConnectorLimit = input.ConnectorLimit,
            Integrations = input.Integrations, ConnectorCategories = input.ConnectorCategories, Features = input.Features,
            CreditValidityDays = input.CreditValidityDays, Rollover = input.Rollover, RenewalAllowed = input.RenewalAllowed,
            AutoTopUpAllowed = input.AutoTopUpAllowed, GraceDays = input.GraceDays, Active = input.Active, EffectiveAt = effective };
        db.PlanVersions.Add(plan);
        return await Save(db, http, "Billing.PlanVersionCreated", plan.Id, plan, ct);
    }

    private static async Task<IResult> Package(PublishPackage input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ValidCode(input.Code) || !ApiSupport.Text(input.Name, 80) || input.Credits is < 1 or > 10_000_000 ||
            !BillingEndpoints.ValidMoney(input.Price) || input.ValidityDays is < 1 or > 3650 ||
            !ValidArray(input.EligiblePlans) || input.EligiblePlans.Any(x => !ValidCode(x)) || !ValidEffective(input.EffectiveAt))
            return ApiSupport.Invalid("package", "Check the package values and effective date.");
        var availableCodes = await db.PlanVersions.Select(x => x.Code).Distinct().ToArrayAsync(ct);
        if (input.EligiblePlans.Except(availableCodes).Any()) return ApiSupport.Invalid("eligiblePlans", "Use existing plan codes.");
        var previous = await db.PackageVersions.Where(x => x.Code == input.Code).OrderByDescending(x => x.Version).FirstOrDefaultAsync(ct);
        var effective = input.EffectiveAt.ToUniversalTime();
        if (previous is not null && effective <= previous.EffectiveAt) return ApiSupport.Invalid("effectiveAt", "Choose a date after the latest version.");
        var package = new PackageVersion { Code = input.Code, Version = (previous?.Version ?? 0) + 1, Name = input.Name.Trim(),
            Credits = input.Credits, Price = input.Price, ValidityDays = input.ValidityDays, EligiblePlans = input.EligiblePlans,
            Active = input.Active, EffectiveAt = effective };
        db.PackageVersions.Add(package);
        return await Save(db, http, "Billing.PackageVersionCreated", package.Id, package, ct);
    }

    private static async Task<IResult> Save(EfiscalDbContext db, HttpContext http, string action, Guid id, object value, CancellationToken ct)
    {
        db.Audit(action, http.UserId(), target: id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error))
        {
            return ApiSupport.Problem(409, "Another version was published. Refresh the catalog before trying again.");
        }
        return Results.Ok(value);
    }

    private static async Task<IResult> Monitor(int? page, EfiscalDbContext db, CreditEngine engine, CancellationToken ct)
    {
        var number = Math.Clamp(page ?? 1, 1, 100_000);
        var offset = (number - 1) * 25;
        var now = DateTimeOffset.UtcNow;
        var subscriptions = await (from sub in db.SubscriberSubscriptions.AsNoTracking()
            join subscriber in db.Subscribers on sub.SubscriberId equals subscriber.Id
            join plan in db.PlanVersions on sub.PlanVersionId equals plan.Id
            select new { sub, subscriber.Name, plan }).OrderByDescending(x => x.sub.StartedAt).ThenBy(x => x.sub.SubscriberId)
            .Skip(offset).Take(25).ToArrayAsync(ct);
        var subscribers = subscriptions.Select(x => x.sub.SubscriberId).ToArray();
        var wallets = await db.CreditWallets.AsNoTracking().Where(x => subscribers.Contains(x.SubscriberId))
            .Select(w => new { w.SubscriberId, w.Id,
                available = db.CreditLots.Where(x => x.WalletId == w.Id && x.EffectiveAt <= now && (x.ExpiresAt == null || x.ExpiresAt > now))
                    .Sum(x => (long)(x.Remaining - x.Reserved)) }).ToArrayAsync(ct);
        var walletStatus = new List<object>();
        foreach (var wallet in wallets)
            walletStatus.Add(new { wallet.SubscriberId, wallet.Id, wallet.available, projectionMismatch = await engine.HasDiscrepancy(wallet.Id, ct) });
        var orders = await db.CheckoutOrders.AsNoTracking().Include(x => x.Payment).OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id)
            .Skip(offset).Take(25).ToArrayAsync(ct);
        return Results.Ok(new { page = number, wallets = walletStatus, total = Math.Max(await db.CheckoutOrders.CountAsync(ct), await db.SubscriberSubscriptions.CountAsync(ct)),
            subscriptions = subscriptions.Select(x => new { x.sub.SubscriberId, x.Name, plan = x.plan.Name, x.sub.PeriodEnd,
                status = CreditEngine.Status(x.sub, x.plan, now) }),
            payments = orders.Select(x => new { x.Id, x.SubscriberId, x.Purpose, x.Amount, x.Currency, x.CreatedAt, x.Payment.Provider,
                x.Payment.ProviderPaymentId, x.Payment.FailureReason, status = CheckoutService.DisplayStatus(x, now).ToString() }) });
    }

    private static async Task<IResult> Adjust(Guid subscriber, WalletAdjustment input, HttpContext http, CreditEngine engine, CancellationToken ct)
    {
        try { await engine.Adjust(subscriber, http.UserId(), input.Quantity, input.Reference, input.Reason, DateTimeOffset.UtcNow, ct); }
        catch (CommercialConflict error) { return ApiSupport.Problem(409, error.Message); }
        return Results.NoContent();
    }

    private static bool ValidCode(string? code) => code is { Length: >= 2 and <= 40 } && code[0] != '-' && code[^1] != '-'
        && code.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    private static bool ValidArray(string[]? list) => list is { Length: <= 50 } && list.All(x => ApiSupport.Text(x, 120))
        && list.Distinct(StringComparer.OrdinalIgnoreCase).Count() == list.Length;
    private static bool ValidEffective(DateTimeOffset date) => date >= DateTimeOffset.UtcNow.AddMinutes(-5) && date < DateTimeOffset.UtcNow.AddYears(10);
}

