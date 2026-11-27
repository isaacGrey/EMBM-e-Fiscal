using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal sealed class CommercialConflict(string message) : Exception(message);

internal sealed class CreditEngine(EfiscalDbContext db)
{
    public static string Status(SubscriberSubscription subscription, PlanVersion plan, DateTimeOffset now)
    {
        if (subscription.Suspended) return "Suspended";
        if (now < subscription.PeriodEnd) return "Active";
        if (subscription.CancelAtPeriodEnd) return "Cancelled";
        return now < subscription.PeriodEnd.AddDays(plan.GraceDays) ? "GracePeriod" : "Expired";
    }

    public async Task<bool> AllowsOperations(Guid subscriberId, DateTimeOffset now, CancellationToken ct)
    {
        var subscription = await db.SubscriberSubscriptions.AsNoTracking().FindSubscriber(subscriberId, ct);
        if (subscription is null)
            return await db.Subscribers.AnyAsync(x => x.Id == subscriberId && x.DevelopmentFixture, ct);
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
        return Status(subscription, plan, now) is "Active" or "GracePeriod";
    }

    public async Task<bool> AllowsConnector(Guid subscriberId, string connector, string category, int connectedCount,
        DateTimeOffset now, CancellationToken ct)
    {
        var plan = await OperationalPlan(subscriberId, now, ct);
        return plan is not null && connectedCount >= 0 && connectedCount < plan.ConnectorLimit &&
            plan.Integrations.Contains(connector) && plan.ConnectorCategories.Contains(category);
    }

    public async Task<bool> AllowsFeature(Guid subscriberId, string feature, DateTimeOffset now, CancellationToken ct) =>
        (await OperationalPlan(subscriberId, now, ct))?.Features.Contains(feature, StringComparer.OrdinalIgnoreCase) == true;

    private async Task<PlanVersion?> OperationalPlan(Guid subscriberId, DateTimeOffset now, CancellationToken ct)
    {
        var subscription = await db.SubscriberSubscriptions.AsNoTracking().FindSubscriber(subscriberId, ct);
        if (subscription is null) return null;
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
        return Status(subscription, plan, now) is "Active" or "GracePeriod" ? plan : null;
    }

    public void Grant(CreditWallet wallet, int quantity, string source, DateTimeOffset effective, DateTimeOffset? expires,
        Guid policy, Guid payment, int slice)
    {
        RequireTransaction();
        if (quantity == 0) return;
        var lot = new CreditLot { SubscriberId = wallet.SubscriberId, WalletId = wallet.Id, PaymentId = payment,
            SubscriptionId = source is "Subscription" or "Renewal" ? wallet.SubscriberId : null, PolicyVersionId = policy, Source = source, Slice = slice,
            Granted = quantity, Remaining = quantity, EffectiveAt = effective, ExpiresAt = expires };
        db.CreditLots.Add(lot);
        var type = source switch { "Subscription" => "SubscriptionGrant", "Renewal" => "RenewalGrant", _ => "TopUpGrant" };
        Record(lot, type, quantity, $"{payment:N}/{slice}", effective);
    }

    public void Allocate(SubscriberSubscription subscription, PlanVersion plan, CreditWallet wallet, DateTimeOffset now)
    {
        if (subscription.Suspended) return;
        var slices = subscription.BillingFrequency == "annual" && plan.AllocationFrequency == "monthly" ? 12 : 1;
        var total = plan.Credits(subscription.BillingFrequency);
        while (subscription.AllocatedSlices < slices)
        {
            var slice = subscription.AllocatedSlices;
            var effective = subscription.PeriodStart.AddMonths(slice);
            if (effective > now) break;
            var ends = slices == 12 ? subscription.PeriodStart.AddMonths(slice + 1) : subscription.PeriodEnd;
            DateTimeOffset? expires = plan.CreditValidityDays is int days ? effective.AddDays(days) : null;
            if (!plan.Rollover && (expires is null || expires > ends)) expires = ends;
            Grant(wallet, total / slices + (slice < total % slices ? 1 : 0),
                subscription.StartedAt == subscription.PeriodStart ? "Subscription" : "Renewal",
                effective, expires, plan.Id, subscription.PeriodPaymentId, slice);
            subscription.AllocatedSlices++;
        }
    }

    public Task<long> Available(Guid walletId, DateTimeOffset now, CancellationToken ct) =>
        EligibleLots(walletId, now).SumAsync(x => (long)x.Remaining - x.Reserved, ct);

    public async Task Refresh(CreditWallet wallet, SubscriberSubscription? subscription, DateTimeOffset now, CancellationToken ct)
    {
        if (subscription is not null)
        {
            Allocate(subscription, await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct), wallet, now);
            await db.SaveChangesAsync(ct);
        }
        await Sweep(wallet, now, ct);
        await db.SaveChangesAsync(ct);
    }

    public Task<bool> HasDiscrepancy(Guid walletId, CancellationToken ct)
    {
        var projection = db.CreditLedgerEntries.Where(x => x.WalletId == walletId).GroupBy(x => x.LotId)
            .Select(group => new
            {
                LotId = (Guid?)group.Key,
                Grants = group.Count(x => x.EventType == "SubscriptionGrant" || x.EventType == "RenewalGrant"
                    || x.EventType == "TopUpGrant" || x.EventType == "AuthorizedAdjustment"),
                Remaining = group.Sum(x => x.EventType == "SubscriptionGrant" || x.EventType == "RenewalGrant"
                    || x.EventType == "TopUpGrant" || x.EventType == "AuthorizedAdjustment" ? x.Quantity
                    : x.EventType == "Consumption" || x.EventType == "Expiry" || x.EventType == "RefundReversal" ? -x.Quantity : 0),
                Reserved = group.Sum(x => x.EventType == "Reservation" ? x.Quantity
                    : x.EventType == "ReservationRelease" || x.EventType == "Consumption" ? -x.Quantity : 0)
            });
        return (from lot in db.CreditLots
                join balance in projection on (Guid?)lot.Id equals balance.LotId into matches
                from balance in matches.DefaultIfEmpty()
                where lot.WalletId == walletId && (balance.LotId == null || balance.Grants != 1
                    || balance.Remaining != lot.Remaining || balance.Reserved != lot.Reserved)
                select lot.Id).AnyAsync(ct);
    }

    private IQueryable<CreditLot> EligibleLots(Guid walletId, DateTimeOffset now) =>
        db.CreditLots.Where(x => x.WalletId == walletId && x.EffectiveAt <= now &&
            (x.ExpiresAt == null || x.ExpiresAt > now) && x.Remaining > x.Reserved);

    public async Task Sweep(CreditWallet wallet, DateTimeOffset now, CancellationToken ct)
    {
        RequireTransaction();
        var stale = await db.CreditReservations.Where(x => x.WalletId == wallet.Id && x.Status == "Reserved" && x.ExpiresAt <= now).ToArrayAsync(ct);
        foreach (var reservation in stale)
        {
            var lot = await db.CreditLots.FindAsync([reservation.LotId], ct) ?? throw new InvalidOperationException();
            Release(lot, reservation, now);
        }
        var expired = await db.CreditLots.Where(x => x.WalletId == wallet.Id && x.ExpiresAt <= now && x.Remaining > 0).ToArrayAsync(ct);
        foreach (var lot in expired)
        {
            Record(lot, "Expiry", lot.Remaining, lot.Id.ToString("N"), now);
            lot.Remaining = 0;
        }
    }

    // The caller owns the transaction and commits the financial document, credit and outbox together.
    public async Task<CreditReservation?> Reserve(Guid subscriber, Guid company, Guid actor, Guid document, string type,
        string action, string operation, DateTimeOffset now, CancellationToken ct)
    {
        RequireTransaction();
        var permission = (type, action) switch
        {
            ("SalesInvoice", "CommitAndSend") => "Invoice.SaveAndSend",
            ("CreditMemo", "CommitAndSend") => "CreditMemo.SaveAndSend",
            ("SalesOrder", "CommitOnly") => "SalesOrder.Save",
            _ => throw new CommercialConflict("The action is unavailable for this document type.")
        };
        await db.LockSubscriberRowAsync(subscriber, ct);
        var authorized = await (
            from membership in db.SubscriberMemberships
            join grant in db.RolePermissions on membership.RoleId equals grant.RoleId
            join workspace in db.Companies on membership.SubscriberId equals workspace.SubscriberId
            join account in db.Subscribers on membership.SubscriberId equals account.Id
            where membership.SubscriberId == subscriber && account.Status == CommercialStatus.Active
                && membership.UserId == actor && workspace.Id == company && grant.PermissionId == permission
                && (membership.RoleId == Permissions.Owner || db.CompanyMemberships.Any(x =>
                    x.SubscriberId == subscriber && x.CompanyId == company && x.UserId == actor))
            select membership).AnyAsync(ct);
        if (!authorized || document == Guid.Empty || !ApiSupport.Text(operation, 128))
            throw new CommercialConflict("The document action is unavailable.");
        var previous = await db.CreditReservations.SingleOrDefaultAsync(x => x.SubscriberId == subscriber && x.OperationKey == operation, ct);
        if (previous is not null)
        {
            if (previous.CompanyId != company || previous.DocumentId != document || previous.DocumentType != type || previous.ActorId != actor)
                throw new CommercialConflict("The operation key belongs to another document or actor.");
            if (previous.Status == "Consumed") return previous;
            if (previous.Status != "Reserved" || previous.ExpiresAt <= now)
                throw new CommercialConflict("The reservation is no longer active. Use a new operation key.");
        }
        if (!await AllowsOperations(subscriber, now, ct)) throw new CommercialConflict("The subscription does not allow document commitment.");
        if (type == "SalesOrder") return null;
        if (previous is not null) return previous;
        if (await db.CreditLedgerEntries.AnyAsync(x => x.SubscriberId == subscriber && x.DocumentId == document
                && x.DocumentType == type && x.EventType == "Consumption", ct))
            throw new CommercialConflict("The document already consumed a credit.");
        var wallet = await db.CreditWallets.SingleOrDefaultAsync(x => x.SubscriberId == subscriber, ct)
            ?? throw new CommercialConflict("A Save & Send wallet is unavailable.");
        var subscription = await db.SubscriberSubscriptions.FindAsync([subscriber], ct);
        await Refresh(wallet, subscription, now, ct);
        var lot = await EligibleLots(wallet.Id, now)
            .OrderBy(x => x.ExpiresAt == null).ThenBy(x => x.ExpiresAt).ThenBy(x => x.EffectiveAt).ThenBy(x => x.Id)
            .FirstOrDefaultAsync(ct) ?? throw new CommercialConflict("No Save & Send Credits are available.");
        var expires = now.AddMinutes(15);
        if (lot.ExpiresAt < expires) expires = lot.ExpiresAt.Value;
        var reservation = new CreditReservation { SubscriberId = subscriber, WalletId = wallet.Id, LotId = lot.Id, CompanyId = company,
            DocumentId = document, DocumentType = type, ActorId = actor, OperationKey = operation, ExpiresAt = expires };
        lot.Reserved++;
        db.CreditReservations.Add(reservation);
        Record(lot, "Reservation", 1, operation, now, reservation);
        return reservation;
    }

    public async Task Complete(CreditReservation reservation, bool committed, DateTimeOffset now, CancellationToken ct)
    {
        RequireTransaction();
        await db.LockSubscriberRowAsync(reservation.SubscriberId, ct);
        if (db.Entry(reservation).State == EntityState.Detached)
            throw new InvalidOperationException("Load the reservation under the subscriber lock.");
        if (reservation.Status == "Consumed" && committed) return;
        if (reservation.Status != "Reserved") throw new CommercialConflict("The reservation is no longer active.");
        var lot = await db.CreditLots.FindAsync([reservation.LotId], ct) ?? throw new InvalidOperationException();
        if (!committed) { Release(lot, reservation, now); return; }
        if (reservation.ExpiresAt <= now) throw new CommercialConflict("The reservation has expired.");
        lot.Reserved--;
        lot.Remaining--;
        reservation.Status = "Consumed";
        Record(lot, "Consumption", 1, reservation.OperationKey, now, reservation);
    }

    public async Task Adjust(Guid subscriber, Guid actor, int quantity, string key, string evidence, DateTimeOffset now, CancellationToken ct)
    {
        if (quantity is < 1 or > 100_000 || !ApiSupport.Text(key, 128) || !ApiSupport.Text(evidence, 500))
            throw new CommercialConflict("A positive adjustment, unique reference and supporting reason are required.");
        await using var tx = await db.LockSubscriberAsync(subscriber, ct);
        var wallet = await db.CreditWallets.SingleOrDefaultAsync(x => x.SubscriberId == subscriber, ct)
            ?? throw new CommercialConflict("The subscriber has no credit wallet.");
        var previous = await db.CreditLedgerEntries.AsNoTracking().SingleOrDefaultAsync(x => x.WalletId == wallet.Id &&
            x.EventType == "AuthorizedAdjustment" && x.IdempotencyKey == key, ct);
        if (previous is not null)
        {
            if (previous.Quantity == quantity && previous.Metadata == evidence && previous.ActorId == actor) return;
            throw new CommercialConflict("The adjustment reference is already assigned.");
        }
        var lot = new CreditLot { SubscriberId = subscriber, WalletId = wallet.Id, Source = "Adjustment", Granted = quantity,
            Remaining = quantity, EffectiveAt = now };
        db.CreditLots.Add(lot);
        db.CreditLedgerEntries.Add(new CreditLedgerEntry { SubscriberId = subscriber, WalletId = wallet.Id, LotId = lot.Id,
            EventType = "AuthorizedAdjustment", Quantity = quantity, SourceReference = "Adjustment", IdempotencyKey = key,
            ActorId = actor, Metadata = evidence, At = now });
        db.Audit("Billing.AuthorizedCreditAdjustment", actor, subscriber, lot.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public void ReverseUnused(CreditLot lot, DateTimeOffset now)
    {
        RequireTransaction();
        if (lot.Remaining == 0) return;
        Record(lot, "RefundReversal", lot.Remaining, lot.Id.ToString("N"), now);
        lot.Remaining = 0;
    }

    private void Release(CreditLot lot, CreditReservation reservation, DateTimeOffset now)
    {
        lot.Reserved--;
        reservation.Status = "Released";
        Record(lot, "ReservationRelease", 1, reservation.OperationKey, now, reservation);
    }

    private void Record(CreditLot lot, string type, int quantity, string key, DateTimeOffset now, CreditReservation? reservation = null)
    {
        db.CreditLedgerEntries.Add(new CreditLedgerEntry { SubscriberId = lot.SubscriberId, WalletId = lot.WalletId, LotId = lot.Id,
            EventType = type, Quantity = quantity, IdempotencyKey = key, SourceReference = lot.Source, At = now, PaymentId = lot.PaymentId,
            CompanyId = reservation?.CompanyId, DocumentId = reservation?.DocumentId, DocumentType = reservation?.DocumentType, ActorId = reservation?.ActorId });
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null) throw new InvalidOperationException("Credit operations require a database transaction.");
    }
}

internal static class BillingQueries
{
    public static Task<SubscriberSubscription?> FindSubscriber(this IQueryable<SubscriberSubscription> query, Guid id, CancellationToken ct) =>
        query.SingleOrDefaultAsync(x => x.SubscriberId == id, ct);
}

