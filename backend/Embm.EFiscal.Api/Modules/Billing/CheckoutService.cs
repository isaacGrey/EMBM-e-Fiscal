using System.Security.Cryptography;
using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

internal record GatewayCapabilities(bool HostedCheckout, bool Refunds, bool Recurring, bool Tokenization, bool Reconciliation);
internal record GatewayEvent(string Key, Guid Order, Guid Customer, Guid? Subscriber, string Reference, decimal Amount, string Currency, string Outcome);
internal record SignedEvent(byte[] Payload, byte[] Signature);

internal interface IPaymentGateway
{
    string Name { get; }
    GatewayCapabilities Capabilities { get; }
    string CreateReference(Guid checkoutId);
    bool TryVerify(SignedEvent notification, out GatewayEvent? paymentEvent);
}

internal sealed class DevelopmentPaymentGateway : IPaymentGateway
{
    private readonly byte[] secret = RandomNumberGenerator.GetBytes(32);
    public string Name => "DevelopmentMock";
    public GatewayCapabilities Capabilities => new(true, true, false, false, true);
    public string CreateReference(Guid checkoutId) => $"mock-{checkoutId:N}";

    public SignedEvent Sign(GatewayEvent paymentEvent)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(paymentEvent);
        return new(payload, HMACSHA256.HashData(secret, payload));
    }

    public bool TryVerify(SignedEvent notification, out GatewayEvent? paymentEvent)
    {
        paymentEvent = null;
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(secret, notification.Payload), notification.Signature))
            return false;
        try { paymentEvent = JsonSerializer.Deserialize<GatewayEvent>(notification.Payload); }
        catch (JsonException) { return false; }
        return paymentEvent is not null;
    }
}

internal record StartCheckout(string Purpose, string OperationKey, Guid? PackageVersionId);

internal sealed class CheckoutService(EfiscalDbContext db, CreditEngine credits, IPaymentGateway gateway)
{
    public const string Initial = "InitialSubscription";
    public const string Renewal = "SubscriptionRenewal";
    public const string TopUp = "CreditTopUp";

    public async Task<CheckoutOrder> Create(Guid userId, Guid? subscriberId, StartCheckout input, DateTimeOffset now, CancellationToken ct)
    {
        if (!ApiSupport.Text(input.OperationKey, 128) || input.OperationKey != input.OperationKey.Trim())
            throw new CommercialConflict("A valid checkout operation key is required.");
        if (subscriberId is null ? input.Purpose != Initial : input.Purpose is not (Renewal or TopUp))
            throw new CommercialConflict("Invalid checkout purpose.");
        if ((input.Purpose == TopUp) != input.PackageVersionId.HasValue)
            throw new CommercialConflict("Select the appropriate package.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockUser(userId, ct);
        if (subscriberId is Guid subscriber) await db.LockSubscriberRowAsync(subscriber, ct);
        var previous = await db.CheckoutOrders.Include(x => x.Payment)
            .SingleOrDefaultAsync(x => x.UserId == userId && x.IdempotencyKey == input.OperationKey, ct);
        if (previous is not null)
        {
            if (previous.Purpose != input.Purpose || previous.PackageVersionId != input.PackageVersionId
                || (input.Purpose != Initial && previous.SubscriberId != subscriberId))
                throw new CommercialConflict("The checkout operation key has already been used.");
            return previous;
        }
        var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId && x.Kind == IdentityKind.Customer && x.Status == IdentityStatus.Verified, ct)
            ?? throw new CommercialConflict("Verify the account before checkout.");
        PlanVersion? plan = null;
        PackageVersion? package = null;
        var frequency = "";
        if (subscriberId is null)
        {
            if (await db.SubscriberMemberships.AnyAsync(x => x.UserId == user.Id && x.RoleId == Permissions.Owner, ct))
                throw new CommercialConflict("An account has already been provisioned.");
            var registration = await db.Registrations.FindAsync([user.Id], ct)
                ?? throw new CommercialConflict("No subscription selection is available.");
            plan = await BillingCatalog.PublishedPlans(db, now).SingleOrDefaultAsync(x => x.Code == registration.PlanId, ct)
                ?? throw new CommercialConflict("The selected plan is no longer published.");
            frequency = registration.Billing;
        }
        else
        {
            var subscription = await db.SubscriberSubscriptions.FindSubscriber(subscriberId.Value, ct)
                ?? throw new CommercialConflict("This account has no paid subscription.");
            plan = await db.PlanVersions.SingleAsync(x => x.Id == subscription.PlanVersionId, ct);
            if (input.Purpose == TopUp)
            {
                if (CreditEngine.Status(subscription, plan, now) is not ("Active" or "GracePeriod"))
                    throw new CommercialConflict("A current subscription is required to buy credits.");
                package = await BillingCatalog.PublishedPackages(db, now).SingleOrDefaultAsync(x => x.Id == input.PackageVersionId, ct);
                if (package is null || !Eligible(package, plan.Code))
                    throw new CommercialConflict("The selected credit package is unavailable for this plan.");
            }
            else
            {
                if (subscription.CancelAtPeriodEnd || now < subscription.PeriodEnd || !plan.RenewalAllowed)
                    throw new CommercialConflict("Renewal is unavailable before the current period ends or while cancellation is scheduled.");
                if (subscription.NextPlanVersionId is Guid next)
                    plan = await db.PlanVersions.SingleAsync(x => x.Id == next, ct);
                frequency = subscription.NextFrequency ?? subscription.BillingFrequency;
                await CheckCapacity(db, subscriberId.Value, plan, ct);
            }
        }
        var order = new CheckoutOrder { UserId = userId, SubscriberId = subscriberId, Purpose = input.Purpose,
            PlanVersionId = plan.Id, PackageVersionId = package?.Id, BillingFrequency = frequency,
            IdempotencyKey = input.OperationKey, Amount = package?.Price ?? plan.Price(frequency), Currency = package?.Currency ?? "PHP",
            CreatedAt = now, ExpiresAt = now.AddMinutes(30) };
        order.Payment = new Payment { OrderId = order.Id, Provider = gateway.Name, ProviderPaymentId = gateway.CreateReference(order.Id),
            Status = PaymentStatus.Pending, CreatedAt = now };
        db.CheckoutOrders.Add(order);
        db.Audit("Billing.CheckoutCreated", userId, subscriberId, order.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return order;
    }

    public static bool Eligible(PackageVersion package, string plan) => package.EligiblePlans.Length == 0 || package.EligiblePlans.Contains(plan);

    public static PaymentStatus DisplayStatus(CheckoutOrder order, DateTimeOffset now) =>
        order.ExpiresAt <= now && order.Payment.Status is PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Processing
            ? PaymentStatus.Expired : order.Payment.Status;

    public async Task<CheckoutOrder> Accept(SignedEvent notification, DateTimeOffset now, CancellationToken ct)
    {
        if (!gateway.TryVerify(notification, out var payload) || payload is null || !ApiSupport.Text(payload.Key, 200))
            throw new CommercialConflict("The payment notification could not be verified.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockUser(payload.Customer, ct);
        var digest = Convert.ToHexString(SHA256.HashData(notification.Payload));
        var order = await db.CheckoutOrders.Include(x => x.Payment).SingleOrDefaultAsync(x => x.Id == payload.Order && x.UserId == payload.Customer, ct)
            ?? throw new CommercialConflict("The payment did not match a checkout.");
        var payment = order.Payment;
        if (payment.Provider != gateway.Name || payment.ProviderPaymentId != payload.Reference || order.Amount != payload.Amount
            || order.Currency != payload.Currency || (order.Purpose == Initial ? null : order.SubscriberId) != payload.Subscriber)
            throw new CommercialConflict("The payment did not match the checkout amount, currency, or account.");
        if (order.SubscriberId is Guid subscriber) await db.LockSubscriberRowAsync(subscriber, ct);
        var previous = await db.PaymentEvents.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == gateway.Name && x.EventKey == payload.Key, ct);
        if (previous is not null)
        {
            if (previous.PaymentId == payment.Id && previous.PayloadDigest == digest) return order;
            throw new CommercialConflict("The notification key was already used for a different event.");
        }
        if (payload.Outcome == "refund")
        {
            await Refund(order, now, ct);
        }
        else if (payment.Status is PaymentStatus.Created or PaymentStatus.Pending or PaymentStatus.Processing)
        {
            payment.Status = DisplayStatus(order, now) == PaymentStatus.Expired ? PaymentStatus.Expired : payload.Outcome switch
            {
                "paid" => PaymentStatus.Paid, "failed" => PaymentStatus.Failed, "cancelled" => PaymentStatus.Cancelled,
                "processing" => PaymentStatus.Processing, _ => throw new CommercialConflict("Unknown payment outcome.")
            };
            if (payment.Status == PaymentStatus.Paid)
            {
                try { await Fulfill(order, now, ct); }
                catch (CommercialConflict error)
                {
                    // Fulfillment has only staged changes. Keep the verified event and flag it for commercial review.
                    db.ChangeTracker.Clear();
                    order = await db.CheckoutOrders.Include(x => x.Payment).SingleAsync(x => x.Id == payload.Order, ct);
                    payment = order.Payment;
                    payment.Status = PaymentStatus.Disputed;
                    payment.FailureReason = error.Message;
                }
                payment.ConfirmedAt = now;
                payment.ProviderTransactionReference = payload.Reference;
            }
            if (payment.Status is PaymentStatus.Failed or PaymentStatus.Expired)
                payment.FailureReason = payment.Status == PaymentStatus.Failed ? "Simulated payment decline" : "Checkout expired";
        }
        else
        {
            throw new CommercialConflict("This checkout is already closed.");
        }
        db.PaymentEvents.Add(new PaymentEvent { PaymentId = payment.Id, Provider = gateway.Name,
            EventKey = payload.Key, EventType = payload.Outcome, PayloadDigest = digest, At = now });
        db.Audit($"Billing.Payment{payment.Status}", order.UserId, order.SubscriberId, payment.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return order;
    }

    private async Task Fulfill(CheckoutOrder order, DateTimeOffset now, CancellationToken ct)
    {
        var plan = await db.PlanVersions.SingleAsync(x => x.Id == order.PlanVersionId, ct);
        if (order.Purpose == Initial)
        {
            if (await db.SubscriberMemberships.AnyAsync(x => x.UserId == order.UserId && x.RoleId == Permissions.Owner, ct))
                throw new CommercialConflict("An account has already been provisioned.");
            var user = await db.Users.SingleAsync(x => x.Id == order.UserId, ct);
            if (user.Status != IdentityStatus.Verified) throw new CommercialConflict("The account is unavailable.");
            var registration = await db.Registrations.SingleAsync(x => x.UserId == user.Id, ct);
            var subscriber = new Subscriber { Name = registration.BusinessName ?? user.FullName, Status = CommercialStatus.Active,
                CreatedAt = now, DevelopmentFixture = gateway.Name == "DevelopmentMock" };
            SetPlan(subscriber, plan, order.BillingFrequency);
            order.SubscriberId = subscriber.Id;
            db.Subscribers.Add(subscriber);
            db.SubscriberMemberships.Add(new SubscriberMembership { SubscriberId = subscriber.Id, UserId = user.Id, RoleId = Permissions.Owner, CreatedAt = now });
            var subscription = new SubscriberSubscription { SubscriberId = subscriber.Id, StartedAt = now };
            SetPeriod(subscription, plan, order, now);
            db.SubscriberSubscriptions.Add(subscription);
            var wallet = new CreditWallet { SubscriberId = subscriber.Id, CreatedAt = now };
            db.CreditWallets.Add(wallet);
            credits.Allocate(subscription, plan, wallet, now);
            db.Audit("Billing.SubscriberProvisioned", user.Id, subscriber.Id, order.Id);
            return;
        }

        var accountId = order.SubscriberId!.Value;
        var current = await db.SubscriberSubscriptions.SingleAsync(x => x.SubscriberId == accountId, ct);
        var currentWallet = await db.CreditWallets.SingleAsync(x => x.SubscriberId == accountId, ct);
        if (order.Purpose == TopUp)
        {
            if (current.PlanVersionId != plan.Id || CreditEngine.Status(current, plan, now) is not ("Active" or "GracePeriod"))
                throw new CommercialConflict("The subscription changed while checkout was open.");
            var package = await db.PackageVersions.SingleAsync(x => x.Id == order.PackageVersionId, ct);
            credits.Grant(currentWallet, package.Credits, "Purchased", now, package.ValidityDays is int days ? now.AddDays(days) : null,
                package.Id, order.Payment.Id, 0);
            return;
        }
        if (current.CancelAtPeriodEnd || (current.NextPlanVersionId ?? current.PlanVersionId) != plan.Id
            || (current.NextFrequency ?? current.BillingFrequency) != order.BillingFrequency || now < current.PeriodEnd)
            throw new CommercialConflict("The renewal checkout is no longer current.");
        await CheckCapacity(db, accountId, plan, ct);
        var previousPlan = await db.PlanVersions.SingleAsync(x => x.Id == current.PlanVersionId, ct);
        credits.Allocate(current, previousPlan, currentWallet, now);
        var account = await db.Subscribers.SingleAsync(x => x.Id == accountId, ct);
        SetPlan(account, plan, order.BillingFrequency);
        SetPeriod(current, plan, order, now);
        current.NextPlanVersionId = null;
        current.NextFrequency = null;
        if (!plan.RenewalAllowed) current.RenewalConsentAt = null;
        if (!plan.AutoTopUpAllowed && await db.AutoTopUpPreferences.FindAsync([accountId], ct) is { } preference)
            preference.Enabled = false;
        credits.Allocate(current, plan, currentWallet, now);
    }

    private async Task Refund(CheckoutOrder order, DateTimeOffset now, CancellationToken ct)
    {
        if (order.Payment.Status != PaymentStatus.Paid || order.SubscriberId is null)
            throw new CommercialConflict("Only a paid simulation can be refunded.");
        var wallet = await db.CreditWallets.SingleAsync(x => x.SubscriberId == order.SubscriberId, ct);
        await credits.Sweep(wallet, now, ct);
        await db.SaveChangesAsync(ct);
        var lots = await db.CreditLots.Where(x => x.PaymentId == order.Payment.Id).ToArrayAsync(ct);
        var consumed = await db.CreditLedgerEntries.AnyAsync(x => x.PaymentId == order.Payment.Id && x.EventType == "Consumption", ct);
        if (consumed || lots.Any(x => x.Reserved > 0))
            throw new CommercialConflict("Credits from this payment are in use. The refund requires review.");
        foreach (var lot in lots) credits.ReverseUnused(lot, now);
        var subscription = await db.SubscriberSubscriptions.FindAsync([order.SubscriberId.Value], ct);
        if (subscription?.PeriodPaymentId == order.Payment.Id) subscription.Suspended = true;
        order.Payment.Status = PaymentStatus.Refunded;
        order.Payment.RefundedAmount = order.Amount;
        order.Payment.RefundedAt = now;
    }

    public static async Task CheckCapacity(EfiscalDbContext db, Guid subscriber, PlanVersion plan, CancellationToken ct)
    {
        var companies = await db.Companies.CountAsync(x => x.SubscriberId == subscriber, ct);
        var users = await db.SubscriberMemberships.CountAsync(x => x.SubscriberId == subscriber, ct);
        var invitations = await db.Invitations.CountAsync(x => x.SubscriberId == subscriber && x.ExpiresAt > DateTimeOffset.UtcNow
            && x.AcceptedAt == null, ct);
        if (companies > plan.CompanyLimit || users + invitations > plan.UserLimit)
            throw new CommercialConflict("The selected plan cannot cover the current company files and user seats.");
    }

    private static void SetPlan(Subscriber subscriber, PlanVersion plan, string frequency)
    {
        subscriber.PlanId = plan.Code;
        subscriber.Billing = frequency;
        subscriber.CompanyLimit = plan.CompanyLimit;
        subscriber.UserLimit = plan.UserLimit;
    }

    private static void SetPeriod(SubscriberSubscription subscription, PlanVersion plan, CheckoutOrder order, DateTimeOffset now)
    {
        subscription.PlanVersionId = plan.Id;
        subscription.BillingFrequency = order.BillingFrequency;
        subscription.PeriodPaymentId = order.Payment.Id;
        subscription.PeriodStart = now;
        subscription.PeriodEnd = now.AddMonths(order.BillingFrequency == "annual" ? 12 : 1);
        subscription.Suspended = false;
        subscription.AllocatedSlices = 0;
    }

    private Task<List<int>> LockUser(Guid userId, CancellationToken ct) =>
        db.Database.SqlQuery<int>($"SELECT 1 AS \"Value\" FROM \"Users\" WHERE \"Id\" = {userId} FOR UPDATE").ToListAsync(ct);
}

