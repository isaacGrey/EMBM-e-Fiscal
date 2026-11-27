using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal sealed class BillingMaintenance(IServiceScopeFactory scopes, ILogger<BillingMaintenance> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var interval = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await interval.WaitForNextTickAsync(stoppingToken))
        {
            try { await ProcessWallets(DateTimeOffset.UtcNow, stoppingToken); }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "Billing maintenance did not complete. The next pass will retry.");
            }
        }
    }

    internal async Task ProcessWallets(DateTimeOffset now, CancellationToken ct)
    {
        await using var searchScope = scopes.CreateAsyncScope();
        var db = searchScope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
        if (db.Database.ProviderName is null) return;
        var dueSubscriptions =
            from subscription in db.SubscriberSubscriptions
            join plan in db.PlanVersions on subscription.PlanVersionId equals plan.Id
            where subscription.BillingFrequency == "annual" && plan.AllocationFrequency == "monthly"
                && !subscription.Suspended && subscription.AllocatedSlices < 12
                && subscription.PeriodStart.AddMonths(subscription.AllocatedSlices) <= now
            select subscription.SubscriberId;
        var dueLots = db.CreditLots.Where(x => x.ExpiresAt <= now && x.Remaining > 0).Select(x => x.SubscriberId);
        var dueReservations = db.CreditReservations.Where(x => x.Status == "Reserved" && x.ExpiresAt <= now).Select(x => x.SubscriberId);
        var subscribers = await dueSubscriptions.Union(dueLots).Union(dueReservations).Order().Take(500).ToArrayAsync(ct);
        foreach (var subscriber in subscribers)
        {
            await using var scope = scopes.CreateAsyncScope();
            var finance = scope.ServiceProvider.GetRequiredService<EfiscalDbContext>();
            var engine = scope.ServiceProvider.GetRequiredService<CreditEngine>();
            await using var tx = await finance.LockSubscriberAsync(subscriber, ct);
            var wallet = await finance.CreditWallets.SingleAsync(x => x.SubscriberId == subscriber, ct);
            var subscription = await finance.SubscriberSubscriptions.FindSubscriber(subscriber, ct);
            await engine.Refresh(wallet, subscription, now, ct);
            await tx.CommitAsync(ct);
        }
    }
}

