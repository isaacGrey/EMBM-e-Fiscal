using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Billing;

public sealed class PlanVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal MonthlyPrice { get; set; }
    public decimal AnnualPrice { get; set; }
    public int MonthlyCredits { get; set; }
    public int AnnualCredits { get; set; }
    public string AllocationFrequency { get; set; } = "period";
    public int CompanyLimit { get; set; }
    public int UserLimit { get; set; }
    public int ConnectorLimit { get; set; }
    public string[] Integrations { get; set; } = [];
    public string[] ConnectorCategories { get; set; } = [];
    public string[] Features { get; set; } = [];
    public int? CreditValidityDays { get; set; }
    public bool Rollover { get; set; }
    public bool RenewalAllowed { get; set; }
    public bool AutoTopUpAllowed { get; set; }
    public int GraceDays { get; set; }
    public bool Active { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public decimal Price(string frequency) => frequency == "annual" ? AnnualPrice : MonthlyPrice;
    public int Credits(string frequency) => frequency == "annual" ? AnnualCredits : MonthlyCredits;
}

public sealed class PackageVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public int Credits { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "PHP";
    public int? ValidityDays { get; set; }
    public string[] EligiblePlans { get; set; } = [];
    public bool Active { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
}

public sealed class CheckoutOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? SubscriberId { get; set; }
    public Guid? PlanVersionId { get; set; }
    public Guid? PackageVersionId { get; set; }
    public string BillingFrequency { get; set; } = "";
    public string Purpose { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "PHP";
    public string IdempotencyKey { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public Payment Payment { get; set; } = null!;
}

public enum PaymentStatus { Created, Pending, Processing, Paid, Failed, Cancelled, Expired, PartiallyRefunded, Refunded, Disputed }
public sealed class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public string Provider { get; set; } = "";
    public string ProviderPaymentId { get; set; } = "";
    public string? ProviderTransactionReference { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public decimal RefundedAmount { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public string? FailureReason { get; set; }
}

public sealed class PaymentEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public string EventKey { get; set; } = "";
    public string EventType { get; set; } = "";
    public string PayloadDigest { get; set; } = "";
    public DateTimeOffset At { get; set; }
}

public sealed class SubscriberSubscription
{
    public Guid SubscriberId { get; set; }
    public Guid PlanVersionId { get; set; }
    public Guid PeriodPaymentId { get; set; }
    public string BillingFrequency { get; set; } = "";
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset PeriodStart { get; set; }
    public DateTimeOffset PeriodEnd { get; set; }
    public bool Suspended { get; set; }
    public bool CancelAtPeriodEnd { get; set; }
    public Guid? NextPlanVersionId { get; set; }
    public string? NextFrequency { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public DateTimeOffset? RenewalConsentAt { get; set; }
    public int AllocatedSlices { get; set; }
}

public sealed class BillingPaymentMethod
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public string Provider { get; set; } = "";
    public string Reference { get; set; } = "";
    public bool Active { get; set; }
}

public sealed class AutoTopUpPreference
{
    public Guid SubscriberId { get; set; }
    public bool Enabled { get; set; }
    public int Threshold { get; set; }
    public Guid? PackageVersionId { get; set; }
    public decimal MaximumAmount { get; set; }
    public decimal PeriodMaximumSpend { get; set; }
    public Guid? PaymentMethodId { get; set; }
    public DateTimeOffset? ConsentAt { get; set; }
    public DateTimeOffset? LastTopUpAt { get; set; }
    public string? LastFailure { get; set; }
}

public sealed class CreditWallet
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class CreditLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid WalletId { get; set; }
    public Guid? SubscriptionId { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? PolicyVersionId { get; set; }
    public string Source { get; set; } = "";
    public int Slice { get; set; }
    public int Granted { get; set; }
    public int Remaining { get; set; }
    public int Reserved { get; set; }
    public DateTimeOffset EffectiveAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class CreditReservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid WalletId { get; set; }
    public Guid LotId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public string DocumentType { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public Guid ActorId { get; set; }
    public string Status { get; set; } = "Reserved";
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CreditLedgerEntry
{
    public long Id { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid WalletId { get; set; }
    public Guid LotId { get; set; }
    public string EventType { get; set; } = "";
    public int Quantity { get; set; }
    public string SourceReference { get; set; } = "";
    public string IdempotencyKey { get; set; } = "";
    public Guid? CompanyId { get; set; }
    public Guid? DocumentId { get; set; }
    public string? DocumentType { get; set; }
    public Guid? PaymentId { get; set; }
    public Guid? ActorId { get; set; }
    public string? Metadata { get; set; }
    public DateTimeOffset At { get; set; }
}

internal static class BillingModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<PlanVersion>().HasIndex(x => new { x.Code, x.Version }).IsUnique();
        b.Entity<PlanVersion>().Property(x => x.MonthlyPrice).HasPrecision(18, 2);
        b.Entity<PlanVersion>().Property(x => x.AnnualPrice).HasPrecision(18, 2);
        b.Entity<PackageVersion>().HasIndex(x => new { x.Code, x.Version }).IsUnique();
        b.Entity<PackageVersion>().Property(x => x.Price).HasPrecision(18, 2);
        b.Entity<CheckoutOrder>().HasIndex(x => new { x.UserId, x.IdempotencyKey }).IsUnique();
        b.Entity<CheckoutOrder>().HasIndex(x => new { x.SubscriberId, x.CreatedAt });
        b.Entity<CheckoutOrder>().Property(x => x.Amount).HasPrecision(18, 2);
        b.Entity<CheckoutOrder>().HasOne(x => x.Payment).WithOne().HasForeignKey<Payment>(x => x.OrderId);
        b.Entity<CheckoutOrder>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<CheckoutOrder>().HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId);
        b.Entity<CheckoutOrder>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId);
        b.Entity<CheckoutOrder>().HasOne<PackageVersion>().WithMany().HasForeignKey(x => x.PackageVersionId);
        b.Entity<Payment>().HasIndex(x => new { x.Provider, x.ProviderPaymentId }).IsUnique();
        b.Entity<Payment>().Property(x => x.Status).HasConversion<string>();
        b.Entity<Payment>().Property(x => x.RefundedAmount).HasPrecision(18, 2);
        b.Entity<PaymentEvent>().HasIndex(x => new { x.Provider, x.EventKey }).IsUnique();
        b.Entity<PaymentEvent>().HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        b.Entity<SubscriberSubscription>().HasKey(x => x.SubscriberId);
        b.Entity<SubscriberSubscription>().HasOne<Subscriber>().WithOne().HasForeignKey<SubscriberSubscription>(x => x.SubscriberId);
        b.Entity<SubscriberSubscription>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.PlanVersionId);
        b.Entity<SubscriberSubscription>().HasOne<PlanVersion>().WithMany().HasForeignKey(x => x.NextPlanVersionId);
        b.Entity<SubscriberSubscription>().HasOne<Payment>().WithMany().HasForeignKey(x => x.PeriodPaymentId);
        b.Entity<BillingPaymentMethod>().HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId);
        b.Entity<BillingPaymentMethod>().HasIndex(x => new { x.SubscriberId, x.Provider }).IsUnique();
        b.Entity<AutoTopUpPreference>().HasKey(x => x.SubscriberId);
        b.Entity<AutoTopUpPreference>().HasOne<Subscriber>().WithOne().HasForeignKey<AutoTopUpPreference>(x => x.SubscriberId);
        b.Entity<AutoTopUpPreference>().Property(x => x.MaximumAmount).HasPrecision(18, 2);
        b.Entity<AutoTopUpPreference>().Property(x => x.PeriodMaximumSpend).HasPrecision(18, 2);
        b.Entity<CreditWallet>().HasOne<Subscriber>().WithOne().HasForeignKey<CreditWallet>(x => x.SubscriberId);
        b.Entity<CreditWallet>().HasAlternateKey(x => new { x.SubscriberId, x.Id });
        b.Entity<CreditLot>().HasAlternateKey(x => new { x.SubscriberId, x.WalletId, x.Id });
        b.Entity<CreditLot>().HasIndex(x => new { x.SubscriberId, x.PaymentId, x.Slice }).IsUnique();
        b.Entity<CreditLot>().HasOne<CreditWallet>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.WalletId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        b.Entity<CreditLot>().HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId);
        b.Entity<CreditLot>().ToTable(t => t.HasCheckConstraint("CK_CreditLot_Balances", "\"Granted\" > 0 AND \"Reserved\" >= 0 AND \"Remaining\" >= \"Reserved\" AND \"Remaining\" <= \"Granted\""));
        b.Entity<CreditReservation>().HasOne<CreditLot>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.WalletId, x.LotId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.WalletId, x.Id });
        b.Entity<CreditReservation>().HasIndex(x => new { x.WalletId, x.OperationKey }).IsUnique();
        b.Entity<CreditLedgerEntry>().HasOne<CreditLot>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.WalletId, x.LotId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.WalletId, x.Id });
        b.Entity<CreditLedgerEntry>().HasIndex(x => new { x.WalletId, x.EventType, x.IdempotencyKey }).IsUnique();
        b.Entity<CreditLedgerEntry>().HasIndex(x => new { x.SubscriberId, x.DocumentType, x.DocumentId }).IsUnique().HasFilter("\"EventType\" = 'Consumption'");
        b.Entity<CreditLedgerEntry>().ToTable(t => t.HasCheckConstraint("CK_CreditLedger_Positive", "\"Quantity\" > 0"));
        BillingCatalog.Seed(b);
    }
}
