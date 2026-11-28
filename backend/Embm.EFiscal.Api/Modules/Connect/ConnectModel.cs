using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal static class ConnectModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<Connector>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(55);
            e.ToTable(t => t.HasCheckConstraint("CK_Connector_Lifecycle", "\"Lifecycle\" IS NULL OR \"Lifecycle\" IN ('Deprecated', 'Retired')"));
        });
        b.Entity<ConnectorVersion>(e =>
        {
            e.HasAlternateKey(x => new { x.ConnectorId, x.Id });
            e.HasIndex(x => new { x.ConnectorId, x.Number }).IsUnique();
            e.HasOne(x => x.Connector).WithMany().HasForeignKey(x => x.ConnectorId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Configuration).HasColumnType("jsonb");
            e.Property(x => x.Specification).HasColumnType("jsonb");
            e.Property(x => x.Validation).HasColumnType("jsonb");
            e.Navigation(x => x.Revocation).AutoInclude();
        });
        b.Entity<ConnectorVersionRevocation>(e =>
        {
            e.ToTable("ConnectorVersionRevocations");
            e.HasKey(x => x.VersionId);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ConnectorVersion>().WithOne(x => x.Revocation).HasForeignKey<ConnectorVersionRevocation>(x => x.VersionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ProviderReadinessEvent>(e =>
        {
            e.ToTable("ProviderReadinessEvents", table => table.HasCheckConstraint("CK_ProviderReadinessEvent_Action", """
                "Sequence" > 0 AND "ConnectorRevision" > 0 AND
                (("Action" = 'EvidenceRecorded' AND "Evidence" IS NOT NULL AND "Reason" IS NULL) OR
                 ("Action" IN ('EvidenceReviewed', 'ChangesRequested', 'Withdrawn') AND "Evidence" IS NULL AND "Reason" IS NOT NULL))
                """));
            e.HasKey(x => new { x.VersionId, x.Sequence });
            e.Property(x => x.Evidence).HasColumnType("jsonb");
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<ConnectorVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<VendorConnectorGrant>(e =>
        {
            e.HasKey(x => new { x.UserId, x.Permission });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });
        b.Entity<ConnectionCredential>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId }).HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ExternalConnection>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ExternalAccountId, x.ExternalResourceId }).IsUnique();
            e.HasIndex(x => x.CredentialReference).IsUnique();
            e.HasOne(x => x.Version).WithMany().HasForeignKey(x => new { x.ConnectorId, Id = x.ConnectorVersionId }).HasPrincipalKey(x => new { x.ConnectorId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ConnectionCredential>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.CredentialReference }).HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId }).HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId }).HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ConnectionMapping>(e =>
        {
            e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Number });
            e.HasOne<ExternalConnection>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId }).HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.FieldMetadata).HasColumnType("jsonb");
        });
        b.Entity<SourceOrder>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.ExternalOrderId }).IsUnique();
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId }).HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Data).HasColumnType("jsonb");
        });
        b.Entity<DesktopAgentRegistration>(e =>
        {
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.PublicKeyFingerprint }).IsUnique();
            e.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId }).HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.InstalledAdapters).HasColumnType("jsonb");
        });
    }
}
