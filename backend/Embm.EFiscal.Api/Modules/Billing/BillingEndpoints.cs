using System.Globalization;
using System.Text;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal record SubscriptionSettings(bool? CancelAtPeriodEnd, bool? AutoRenew, bool? StoreDemoMethod, Guid? NextPlanVersionId, string? NextFrequency, bool? ClearChange);
internal record TopUpSettings(bool Enabled, int Threshold, Guid? PackageVersionId, decimal MaximumAmount, decimal PeriodMaximumSpend, bool Consent);

internal static class BillingEndpoints
{
    public static void MapBilling(this WebApplication app)
    {
        app.MapGet("/api/billing/catalog", Catalog).AllowAnonymous().AddEndpointFilter(ApiSupport.Protect);
        var client = app.MapGroup("/api/billing").RequireAuthorization("customer").AddEndpointFilter(ApiSupport.Protect);
        var workspace = client.MapGroup("/subscribers/{subscriber:guid}");
        workspace.MapGet("/summary", Summary);
        workspace.MapGet("/payments", Payments);
        workspace.MapGet("/provider-records", ProviderBilling.Records);
        workspace.MapGet("/credits", Ledger);
        workspace.MapPut("/subscription", Settings);
        workspace.MapPut("/auto-top-up", AutoTopUp);
        if (app.Environment.IsDevelopment())
        {
            client.MapPost("/checkout", StartInitial);
            workspace.MapPost("/checkout", StartWorkspace);
            client.MapGet("/checkout/{id:guid}", GetCheckout);
            client.MapPost("/checkout/{id:guid}/simulate/{outcome}", Simulate);
            app.MapVendorBilling();
        }
    }

    private static async Task<IResult> Catalog(EfiscalDbContext db, IWebHostEnvironment environment, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var plans = await BillingCatalog.PublishedPlans(db, now).OrderBy(x => x.MonthlyPrice).ToArrayAsync(ct);
        var packages = await BillingCatalog.PublishedPackages(db, now).OrderBy(x => x.Credits).ToArrayAsync(ct);
        return Results.Ok(new { plans = plans.Select(BillingCatalog.PublicPlan), packages = packages.Select(PublicPackage), checkoutAvailable = environment.IsDevelopment() });
    }

    private static object PublicPackage(PackageVersion x) => new { id = x.Code, versionId = x.Id, x.Name, x.Credits, x.Price,
        x.Currency, x.ValidityDays, x.EligiblePlans, x.Version };

    private static Task<IResult> StartInitial(StartCheckout input, HttpContext http, EfiscalDbContext db, CheckoutService service, CancellationToken ct) =>
        Start(null, input, http, db, service, ct);

    private static async Task<IResult> StartWorkspace(Guid subscriber, StartCheckout input, HttpContext http, EfiscalDbContext db,
        CheckoutService service, CancellationToken ct)
    {
        var permission = input.Purpose == CheckoutService.TopUp ? "Credits.Purchase" : "Billing.Manage";
        if (await TenantAccess.ResolveAsync(db, http, subscriber, permission, ct) is null) return TenantAccess.Missing();
        return await Start(subscriber, input, http, db, service, ct);
    }

    private static async Task<IResult> Start(Guid? subscriber, StartCheckout input, HttpContext http, EfiscalDbContext db, CheckoutService service, CancellationToken ct)
    {
        try { return Results.Ok(await CheckoutDto(db, await service.Create(http.UserId(), subscriber, input, DateTimeOffset.UtcNow, ct), ct)); }
        catch (CommercialConflict error) { return ApiSupport.Problem(409, error.Message); }
    }

    private static async Task<CheckoutOrder?> OwnedCheckout(Guid id, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        var order = await db.CheckoutOrders.AsNoTracking().Include(x => x.Payment).SingleOrDefaultAsync(x => x.Id == id && x.UserId == http.UserId(), ct);
        if (order is null || order.Purpose == CheckoutService.Initial) return order;
        var permission = order.Purpose == CheckoutService.TopUp ? "Credits.Purchase" : "Billing.Manage";
        return order.SubscriberId is Guid sid && await TenantAccess.ResolveAsync(db, http, sid, permission, ct) is not null ? order : null;
    }

    private static async Task<IResult> GetCheckout(Guid id, EfiscalDbContext db, HttpContext http, CancellationToken ct)
    {
        var order = await OwnedCheckout(id, db, http, ct);
        return order is null ? TenantAccess.Missing() : Results.Ok(await CheckoutDto(db, order, ct));
    }

    private static async Task<IResult> Simulate(Guid id, string outcome, EfiscalDbContext db, HttpContext http,
        DevelopmentPaymentGateway gateway, CheckoutService service, CancellationToken ct)
    {
        var order = await OwnedCheckout(id, db, http, ct);
        if (order is null) return TenantAccess.Missing();
        if (outcome is not ("paid" or "failed" or "cancelled" or "processing" or "duplicate" or "refund"))
            return ApiSupport.Invalid("outcome", "Choose a supported simulated payment outcome.");
        var duplicate = outcome == "duplicate";
        if (duplicate) outcome = "paid";
        var notification = gateway.Sign(new($"{id:N}/{outcome}", id, order.UserId,
            order.Purpose == CheckoutService.Initial ? null : order.SubscriberId, order.Payment.ProviderPaymentId, order.Amount, order.Currency, outcome));
        try
        {
            var confirmed = await service.Accept(notification, DateTimeOffset.UtcNow, ct);
            if (duplicate)
            {
                db.ChangeTracker.Clear();
                confirmed = await service.Accept(notification, DateTimeOffset.UtcNow, ct);
            }
            return Results.Ok(await CheckoutDto(db, confirmed, ct));
        }
        catch (CommercialConflict error) { return ApiSupport.Problem(409, error.Message); }
    }

    private static async Task<object> CheckoutDto(EfiscalDbContext db, CheckoutOrder order, CancellationToken ct)
    {
        var plan = await db.PlanVersions.AsNoTracking().SingleAsync(x => x.Id == order.PlanVersionId, ct);
        var package = order.PackageVersionId is Guid packageId ? await db.PackageVersions.AsNoTracking().SingleAsync(x => x.Id == packageId, ct) : null;
        return new { order.Id, order.SubscriberId, order.Purpose, order.Amount, order.Currency, order.BillingFrequency,
            plan = plan.Name, package = package?.Name, credits = package?.Credits ?? plan.Credits(order.BillingFrequency),
            order.Payment.Provider, order.Payment.ProviderPaymentId, order.Payment.ConfirmedAt, order.Payment.RefundedAmount,
            order.Payment.FailureReason, status = CheckoutService.DisplayStatus(order, DateTimeOffset.UtcNow).ToString(), order.ExpiresAt };
    }

    private static async Task<IResult> Summary(Guid subscriber, HttpContext http, EfiscalDbContext db, CreditEngine engine, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriber, "Billing.View", ct) is null) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var subscription = await db.SubscriberSubscriptions.FindSubscriber(subscriber, ct);
        var wallet = await db.CreditWallets.SingleOrDefaultAsync(x => x.SubscriberId == subscriber, ct);
        if (subscription is null || wallet is null) return Results.Ok(new { available = false });
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
        var nextPlan = subscription.NextPlanVersionId is Guid next ? await db.PlanVersions.SingleAsync(x => x.Id == next, ct) : plan;
        await engine.Refresh(wallet, subscription, now, ct);
        var lots = await db.CreditLots.AsNoTracking().Where(x => x.WalletId == wallet.Id && x.EffectiveAt <= now).OrderBy(x => x.EffectiveAt).ThenBy(x => x.Id)
            .Select(x => new { x.Id, x.Source, x.Granted, x.Remaining, x.Reserved, x.EffectiveAt, x.ExpiresAt }).ToArrayAsync(ct);
        var totals = await db.CreditLedgerEntries.Where(x => x.WalletId == wallet.Id && (x.EventType == "Consumption" || x.EventType == "Expiry"))
            .GroupBy(x => x.EventType).Select(g => new { Event = g.Key, Quantity = g.Sum(x => (long)x.Quantity) }).ToDictionaryAsync(x => x.Event, x => x.Quantity, ct);
        var topUp = await db.AutoTopUpPreferences.AsNoTracking().SingleOrDefaultAsync(x => x.SubscriberId == subscriber, ct);
        var companies = await db.Companies.Where(x => x.SubscriberId == subscriber).OrderBy(x => x.RegisteredName).Select(x => new { x.Id, name = x.RegisteredName }).ToArrayAsync(ct);
        var users = await db.SubscriberMemberships.CountAsync(x => x.SubscriberId == subscriber, ct);
        var renewal = await db.CheckoutOrders.Where(x => x.SubscriberId == subscriber && x.Purpose == CheckoutService.Renewal
                && x.CreatedAt >= subscription.PeriodEnd).OrderByDescending(x => x.CreatedAt).Select(x => (PaymentStatus?)x.Payment.Status).FirstOrDefaultAsync(ct);
        var method = await db.BillingPaymentMethods.AnyAsync(x => x.SubscriberId == subscriber && x.Id == subscription.PaymentMethodId && x.Active, ct);
        var packages = await BillingCatalog.PublishedPackages(db, now).Where(x => x.EligiblePlans.Length == 0 || x.EligiblePlans.Contains(plan.Code))
            .OrderBy(x => x.Credits).ToArrayAsync(ct);
        await tx.CommitAsync(ct);
        var status = CreditEngine.Status(subscription, plan, now);
        return Results.Ok(new
        {
            available = true, renewalStatus = (status is "Expired" or "GracePeriod") && renewal == PaymentStatus.Failed ? "PastDue" : renewal?.ToString(), plan = BillingCatalog.PublicPlan(plan), status, subscription.BillingFrequency, subscription.StartedAt,
            subscription.PeriodStart, subscription.PeriodEnd, subscription.CancelAtPeriodEnd, subscription.RenewalConsentAt,
            subscription.NextPlanVersionId, subscription.NextFrequency, nextPlanName = nextPlan.Name,
            renewalAmount = nextPlan.Price(subscription.NextFrequency ?? subscription.BillingFrequency), hasPaymentMethod = method,
            renewalAvailable = now >= subscription.PeriodEnd && !subscription.CancelAtPeriodEnd && plan.RenewalAllowed,
            currentIncluded = plan.Credits(subscription.BillingFrequency), companies, users, topUp, packages = packages.Select(PublicPackage),
            wallet = new { available = lots.Sum(x => (long)x.Remaining - x.Reserved), reserved = lots.Sum(x => (long)x.Reserved),
                included = lots.Where(x => x.Source is "Subscription" or "Renewal").Sum(x => (long)x.Remaining),
                purchased = lots.Where(x => x.Source == "Purchased").Sum(x => (long)x.Remaining), used = totals.GetValueOrDefault("Consumption"),
                expired = totals.GetValueOrDefault("Expiry"), lots }
        });
    }

    private static async Task<IResult> Settings(Guid subscriber, SubscriptionSettings input, HttpContext http, IWebHostEnvironment environment,
        EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriber, "Billing.Manage", ct) is null) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var subscription = await db.SubscriberSubscriptions.FindSubscriber(subscriber, ct);
        if (subscription is null) return TenantAccess.Missing();
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
        if (input.NextPlanVersionId.HasValue != (input.NextFrequency is "monthly" or "annual") ||
            (!input.NextPlanVersionId.HasValue && input.NextFrequency is not null) || (input.ClearChange == true && input.NextPlanVersionId.HasValue))
            return ApiSupport.Invalid("nextPlanVersionId", "Select a plan and billing cycle.");
        if (input.NextPlanVersionId is Guid nextId)
        {
            var next = await BillingCatalog.PublishedPlans(db, now).SingleOrDefaultAsync(x => x.Id == nextId, ct);
            if (next is null) return ApiSupport.Invalid("nextPlanVersionId", "Select a published plan.");
            try { await CheckoutService.CheckCapacity(db, subscriber, next, ct); }
            catch (CommercialConflict error) { return ApiSupport.Problem(409, error.Message); }
            subscription.NextPlanVersionId = next.Id;
            subscription.NextFrequency = input.NextFrequency;
        }
        if (input.ClearChange == true)
        {
            subscription.NextPlanVersionId = null;
            subscription.NextFrequency = null;
        }
        if (input.CancelAtPeriodEnd is bool cancel)
        {
            subscription.CancelAtPeriodEnd = cancel;
            if (cancel) subscription.RenewalConsentAt = null;
        }
        if (input.StoreDemoMethod is bool store)
        {
            if (!environment.IsDevelopment()) return ApiSupport.Problem(409, "Payment methods are unavailable.");
            var renewal = await db.CheckoutOrders.Where(x => x.SubscriberId == subscriber && x.Purpose == CheckoutService.Renewal
                && x.CreatedAt >= subscription.PeriodEnd).OrderByDescending(x => x.CreatedAt).Select(x => (PaymentStatus?)x.Payment.Status).FirstOrDefaultAsync(ct);
        var method = await db.BillingPaymentMethods.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.Provider == "DevelopmentMock", ct);
            if (method is null && store)
            {
                method = new BillingPaymentMethod { SubscriberId = subscriber, Provider = "DevelopmentMock", Reference = Guid.NewGuid().ToString("N") };
                db.BillingPaymentMethods.Add(method);
            }
            if (method is not null) method.Active = store;
            subscription.PaymentMethodId = store ? method!.Id : null;
            if (!store)
            {
                subscription.RenewalConsentAt = null;
                var pref = await db.AutoTopUpPreferences.FindAsync([subscriber], ct);
                if (pref is not null) { pref.Enabled = false; pref.PaymentMethodId = null; pref.ConsentAt = null; }
            }
        }
        if (input.AutoRenew is bool renew)
        {
            if (renew && (!plan.RenewalAllowed || subscription.PaymentMethodId is null || subscription.CancelAtPeriodEnd))
                return ApiSupport.Invalid("autoRenew", "Add an authorized payment method and resume renewal before consenting.");
            subscription.RenewalConsentAt = renew ? now : null;
        }
        db.Audit("Billing.SubscriptionSettingsChanged", http.UserId(), subscriber);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> AutoTopUp(Guid subscriber, TopUpSettings input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriber, "Credits.Purchase", ct) is null) return TenantAccess.Missing();
        var now = DateTimeOffset.UtcNow;
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var subscription = await db.SubscriberSubscriptions.FindSubscriber(subscriber, ct);
        if (subscription is null) return TenantAccess.Missing();
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
        var setting = await db.AutoTopUpPreferences.FindAsync([subscriber], ct);
        if (setting is null)
        {
            setting = new AutoTopUpPreference { SubscriberId = subscriber };
            db.AutoTopUpPreferences.Add(setting);
        }
        if (input.Enabled)
        {
            var package = await BillingCatalog.PublishedPackages(db, now).SingleOrDefaultAsync(x => x.Id == input.PackageVersionId, ct);
            if (!plan.AutoTopUpAllowed || !input.Consent || subscription.PaymentMethodId is null || subscription.CancelAtPeriodEnd
                || CreditEngine.Status(subscription, plan, now) is not ("Active" or "GracePeriod")
                || package is null || !CheckoutService.Eligible(package, plan.Code) || input.Threshold < 0
                || !ValidMoney(input.MaximumAmount) || !ValidMoney(input.PeriodMaximumSpend)
                || package.Price > input.MaximumAmount || input.MaximumAmount > input.PeriodMaximumSpend)
                return ApiSupport.Invalid("enabled", "Choose an eligible package, valid spending controls, a payment method and explicit consent.");
            setting.ConsentAt = now;
            setting.Threshold = input.Threshold;
            setting.PackageVersionId = package.Id;
            setting.MaximumAmount = input.MaximumAmount;
            setting.PeriodMaximumSpend = input.PeriodMaximumSpend;
            setting.PaymentMethodId = subscription.PaymentMethodId;
        }
        else setting.ConsentAt = null;
        setting.Enabled = input.Enabled;
        db.Audit("Billing.AutoTopUpSettingsChanged", http.UserId(), subscriber);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    internal static bool ValidMoney(decimal amount) => amount > 0 && amount <= 9999999999999999m && decimal.Round(amount, 2) == amount;

    private static async Task<IResult> Payments(Guid subscriber, string? status, string? purpose, int? page, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriber, "Billing.View", ct) is null) return TenantAccess.Missing();
        var number = Math.Clamp(page ?? 1, 1, 100_000);
        var now = DateTimeOffset.UtcNow;
        var query = from order in db.CheckoutOrders.AsNoTracking()
            join plan in db.PlanVersions on order.PlanVersionId equals plan.Id
            join package in db.PackageVersions on order.PackageVersionId equals package.Id into match
            from package in match.DefaultIfEmpty()
            where order.SubscriberId == subscriber
            select new { order.Id, order.CreatedAt, order.Purpose, plan = plan.Name, package = package == null ? null : package.Name, order.Amount, order.Currency,
                order.Payment.Provider, order.Payment.ProviderPaymentId, order.Payment.ConfirmedAt, order.Payment.RefundedAmount,
                order.Payment.FailureReason, Status = order.ExpiresAt <= now &&
                    (order.Payment.Status == PaymentStatus.Created || order.Payment.Status == PaymentStatus.Pending || order.Payment.Status == PaymentStatus.Processing)
                    ? PaymentStatus.Expired : order.Payment.Status };
        if (!string.IsNullOrEmpty(purpose)) query = query.Where(x => x.Purpose == purpose);
        if (!string.IsNullOrEmpty(status))
        {
            if (!Enum.TryParse<PaymentStatus>(status, out var filter) || !Enum.IsDefined(filter))
                return ApiSupport.Invalid("status", "Choose a valid payment status.");
            query = query.Where(x => x.Status == filter);
        }
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((number - 1) * 25).Take(25).ToArrayAsync(ct);
        return Results.Ok(new { page = number, total, items = rows.Select(x => new { x.Id, x.CreatedAt, x.Purpose, x.plan, x.package,
            x.Amount, x.Currency, x.Provider, x.ProviderPaymentId, x.ConfirmedAt, x.RefundedAmount, x.FailureReason, status = x.Status.ToString() }) });
    }

    private static async Task<IResult> Ledger(Guid subscriber, Guid? company, string? documentType, string? search,
        DateOnly? from, DateOnly? through, int? page, bool? export, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var grant = await TenantAccess.ResolveAsync(db, http, subscriber, "Credits.View", ct);
        if (grant is null) return TenantAccess.Missing();
        if (company.HasValue && !await TenantAccess.Companies(db, subscriber, http.UserId(), grant).AnyAsync(x => x.Id == company, ct)) return TenantAccess.Missing();
        if (!ApiSupport.Optional(search, 128) || (from > through) || through == DateOnly.MaxValue)
            return ApiSupport.Invalid("filter", "Choose a valid search or date range.");
        var query = db.CreditLedgerEntries.AsNoTracking().Where(x => x.SubscriberId == subscriber);
        if (company.HasValue) query = query.Where(x => x.CompanyId == company);
        if (!string.IsNullOrEmpty(documentType)) query = query.Where(x => x.DocumentType == documentType);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
            query = query.Where(x => EF.Functions.ILike(x.IdempotencyKey, $"%{term}%", @"\") ||
                EF.Functions.ILike(x.EventType, $"%{term}%", @"\") || EF.Functions.ILike(x.SourceReference, $"%{term}%", @"\") ||
                (x.DocumentId.HasValue && EF.Functions.ILike(x.DocumentId.Value.ToString(), $"%{term}%", @"\")));
        }
        if (from is DateOnly first) { var date = ManilaDate(first); query = query.Where(x => x.At >= date); }
        if (through is DateOnly last) { var date = ManilaDate(last.AddDays(1)); query = query.Where(x => x.At < date); }
        var number = Math.Clamp(page ?? 1, 1, 100_000);
        var sorted = query.OrderByDescending(x => x.At).ThenByDescending(x => x.Id);
        var total = await query.CountAsync(ct);
        if (export == true && total > 10_000) return ApiSupport.Invalid("export", "Narrow the filters to at most 10,000 entries.");
        var rows = await (export == true ? sorted.Take(10_000) : sorted.Skip((number - 1) * 25).Take(25)).ToArrayAsync(ct);
        if (export != true) return Results.Ok(new { page = number, total, items = rows });
        var csv = new StringBuilder("Date,Event,Quantity,Source,Company,Document,Document type,Payment\r\n");
        foreach (var row in rows)
            csv.AppendLine(string.Join(",", Csv(row.At.ToString("O", CultureInfo.InvariantCulture)), Csv(row.EventType),
                row.Quantity.ToString(CultureInfo.InvariantCulture), Csv(row.SourceReference), Csv(row.CompanyId?.ToString()),
                Csv(row.DocumentId?.ToString()), Csv(row.DocumentType), Csv(row.PaymentId?.ToString())));
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "save-and-send-credit-history.csv");
    }

    private static DateTimeOffset ManilaDate(DateOnly day) => new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(8)).ToUniversalTime();
    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

