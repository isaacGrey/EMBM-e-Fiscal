using System.Text.Json;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class SyncDestination
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ProfileId { get; set; }
    public MappingProfile Profile { get; set; } = null!;
    public string DocumentType { get; set; } = "";
    public bool Enabled { get; set; }
    public bool Automatic { get; set; }
    public string Scenario { get; set; } = "Normal";
}

internal sealed class SyncDestinationWindow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ProfileId { get; set; }
    public MappingProfile Profile { get; set; } = null!;
    public string DocumentType { get; set; } = "";
    public DateOnly Start { get; set; }
    public DateOnly? End { get; set; }
    public bool Automatic { get; set; }
    public Guid? ClosedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid ApprovedBy { get; set; }
    public DateTimeOffset ApprovedAt { get; set; }
    public string Reason { get; set; } = "";
}

internal sealed class SyncOperation
{
    public Guid Id { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid ConnectorVersionId { get; set; }
    public Guid ProfileId { get; set; }
    public int MappingNumber { get; set; }
    public Guid? ResponseProfileId { get; set; }
    public int? ResponseMappingNumber { get; set; }
    public string Scenario { get; set; } = "Normal";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
}

internal sealed class SyncAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OperationId { get; set; }
    public int Number { get; set; }
    public int MappingNumber { get; set; }
    public string Status { get; set; } = "Processing";
    public string? Category { get; set; }
    public string? Evidence { get; set; }
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

internal sealed class ExternalDocumentIdentity
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DocumentId { get; set; }
    public Guid OperationId { get; set; }
    public Guid ConnectionId { get; set; }
    public string InternalNumber { get; set; } = "";
    public string ExternalAccount { get; set; } = "";
    public string ExternalCompany { get; set; } = "";
    public string ExternalType { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string ExternalNumber { get; set; } = "";
    public string Status { get; set; } = "";
    public Guid ConnectorVersionId { get; set; }
    public int MappingNumber { get; set; }
    public string EvidenceHash { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
}

internal sealed class MockAccountingRecord
{
    public Guid OperationId { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public Guid DocumentId { get; set; }
    public string Number { get; set; } = "";
    public string ExternalType { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string ExternalNumber { get; set; } = "";
    public string Status { get; set; } = "Posted";
    public string? Relationship { get; set; }
    public decimal Total { get; set; }
    public string PayloadHash { get; set; } = "";
    public JsonElement? PostedState { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal static class SyncModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<DocumentOutboxMessage>().HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        b.Entity<SyncDestination>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentType, x.BranchId }).IsUnique().AreNullsDistinct(false);
            e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ProfileId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SyncDestinationWindow>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentType, x.BranchId, x.Start });
            e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ProfileId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SyncOperation>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.DocumentId }).IsUnique();
            e.HasOne<DocumentOutboxMessage>().WithOne().HasForeignKey<SyncOperation>(x => new { x.SubscriberId, x.CompanyId, x.Id })
                .HasPrincipalKey<DocumentOutboxMessage>(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MappingVersion>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.ProfileId, Number = x.MappingNumber })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<MappingVersion>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, ProfileId = x.ResponseProfileId, Number = x.ResponseMappingNumber })
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExternalConnection>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SyncAttempt>(e =>
        {
            e.HasIndex(x => new { x.OperationId, x.Number }).IsUnique();
            e.HasOne<SyncOperation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.OperationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ExternalDocumentIdentity>(e =>
        {
            e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.DocumentId });
            e.HasIndex(x => new { x.ConnectionId, x.ExternalType, x.ExternalId }).IsUnique();
            e.HasOne<SyncOperation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.OperationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FiscalDocument>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.DocumentId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MockAccountingRecord>(e =>
        {
            e.HasKey(x => x.OperationId);
            e.HasIndex(x => new { x.ConnectionId, x.ExternalType, x.Number }).IsUnique();
            e.HasOne<SyncOperation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.OperationId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Total).HasPrecision(28, 8);
            e.Property(x => x.PostedState).HasColumnType("jsonb");
        });
    }
}

