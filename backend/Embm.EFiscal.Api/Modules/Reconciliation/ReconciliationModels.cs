using System.Text.Json;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reconciliation;

internal sealed class CoverageRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public ExternalConnection Connection { get; set; } = null!;
    public int Number { get; set; }
    public DateOnly Start { get; set; }
    public DateOnly? End { get; set; }
    public DateOnly Adoption { get; set; }
    public string Mode { get; set; } = "EfiscalOnly";
    public string[] Types { get; set; } = [];
    public string IssuanceScope { get; set; } = "Adopted";
    public string[] ExcludedSources { get; set; } = [];
    public string? ExclusionReason { get; set; }
    public string TimeZone { get; set; } = "Asia/Manila";
    public string Reason { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset? EffectiveAt { get; set; }
    public decimal AmountTolerance { get; set; }
    public int FreshnessHours { get; set; } = 48;
    public int? IntervalHours { get; set; }
    public bool AfterSynchronization { get; set; }
    public int BatchSize { get; set; } = 25;
}

internal sealed class AccountingObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid ConnectorVersionId { get; set; }
    public int? NormalizationVersion { get; set; }
    public string Account { get; set; } = "";
    public string ExternalCompany { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Number { get; set; } = "";
    public string Type { get; set; } = "";
    public DateOnly? BusinessDate { get; set; }
    public string Source { get; set; } = "";
    public DateTimeOffset? ModifiedAt { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
    public string ProtectedValues { get; set; } = "";
    public string Hash { get; set; } = "";
    public string Evidence { get; set; } = "";
}

internal sealed class ReconciliationCheck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? ObservationId { get; set; }
    public Guid? CoverageId { get; set; }
    public Guid? JobId { get; set; }
    public Guid? BranchId { get; set; }
    public string UnitKey { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Number { get; set; }
    public string? ExternalNumber { get; set; }
    public DateOnly? BusinessDate { get; set; }
    public string? Currency { get; set; }
    public decimal? ExpectedTotal { get; set; }
    public decimal? ActualTotal { get; set; }
    public string Coverage { get; set; } = "Eligible";
    public string Retrieval { get; set; } = "NotAttempted";
    public string Completeness { get; set; } = "Pending";
    public string Status { get; set; } = "NotYetReconciled";
    public string ProtectedComparison { get; set; } = "";
    public string[] Categories { get; set; } = [];
    public int FindingCount { get; set; }
    public bool ExternalChanged { get; set; }
    public DateTimeOffset FreshUntil { get; set; }
    public string Synchronization { get; set; } = "NotApplicable";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ReconciliationFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid CheckId { get; set; }
    public string UnitKey { get; set; } = "";
    public string Category { get; set; } = "";
    public string Field { get; set; } = "";
    public string Severity { get; set; } = "Review";
    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ReconciliationReview
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FindingId { get; set; }
    public Guid? Reviewer { get; set; }
    public Guid? Actor { get; set; }
    public Guid? VerificationId { get; set; }
    public string Status { get; set; } = "";
    public string? ProtectedNote { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ReconciliationJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? CoverageId { get; set; }
    public string Key { get; set; } = "";
    public string Kind { get; set; } = "Document";
    public string State { get; set; } = "Queued";
    public string? Failure { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? Through { get; set; }
    public string? Cursor { get; set; }
    public Guid? DocumentCursor { get; set; }
    public int Attempts { get; set; }
    public Guid? Lease { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset AvailableAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class AccountingReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ObservationId { get; set; }
    public Guid ActorId { get; set; }
    public string Purpose { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class AccountingMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ObservationId { get; set; }
    public string ExternalId { get; set; } = "";
    public string Type { get; set; } = "";
    public Guid ActorId { get; set; }
    public string ProtectedReason { get; set; } = "";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class MockAccountingRevision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid OperationId { get; set; }
    public int Number { get; set; }
    public string Scenario { get; set; } = "Exact";
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
}

internal static class ReconciliationModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<CoverageRevision>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Number }).IsUnique();
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.AmountTolerance).HasPrecision(18, 8);
        });
        b.Entity<AccountingObservation>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Type, x.ExternalId, x.RetrievedAt });
            e.HasOne<ExternalConnection>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReconciliationCheck>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.UnitKey, x.At });
            e.HasIndex(x => new { x.JobId, x.UnitKey }).IsUnique();
            e.Property(x => x.ExpectedTotal).HasPrecision(28, 8);
            e.Property(x => x.ActualTotal).HasPrecision(28, 8);
            e.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AccountingObservation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ObservationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CoverageRevision>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.CoverageId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ReconciliationJob>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.JobId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReconciliationFinding>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.UnitKey, x.Category, x.Field });
            e.HasOne<ReconciliationCheck>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.CheckId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReconciliationReview>(e =>
        {
            e.HasIndex(x => new { x.FindingId, x.At });
            e.HasOne<ReconciliationFinding>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.FindingId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ReconciliationCheck>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.VerificationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReconciliationJob>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Key }).IsUnique();
            e.HasIndex(x => new { x.State, x.AvailableAt });
            e.HasOne<ExternalConnection>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CoverageRevision>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.CoverageId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AccountingReference>(e => e.HasOne<AccountingObservation>().WithMany()
            .HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ObservationId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict));
        b.Entity<AccountingMatch>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId }).IsUnique();
            e.HasIndex(x => new { x.ConnectionId, x.Type, x.ExternalId }).IsUnique();
            e.HasOne<AccountingObservation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ObservationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MockAccountingRevision>(e =>
        {
            e.HasIndex(x => new { x.OperationId, x.Number }).IsUnique();
            e.HasOne<SyncOperation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.OperationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
    }
}

