using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.CompanyConfiguration;
using Embm.EFiscal.Api.Modules.Connect;
using Embm.EFiscal.Api.Modules.Documents;
using Embm.EFiscal.Api.Modules.Identity;
using Embm.EFiscal.Api.Modules.MasterData;
using Embm.EFiscal.Api.Modules.Reconciliation;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Infrastructure;

public sealed class EfiscalDbContext(DbContextOptions<EfiscalDbContext> options) : DbContext(options)
{
    public DbSet<Modules.Reporting.ReportingPreparation> ReportingPreparations => Set<Modules.Reporting.ReportingPreparation>();
    public DbSet<Modules.Reporting.ReportingSimulation> ReportingSimulations => Set<Modules.Reporting.ReportingSimulation>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserIdentity> UserIdentities => Set<UserIdentity>();
    public DbSet<Registration> Registrations => Set<Registration>();
    public DbSet<IdentityToken> IdentityTokens => Set<IdentityToken>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<SecurityAudit> SecurityAudits => Set<SecurityAudit>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();
    public DbSet<SubscriberMembership> SubscriberMemberships => Set<SubscriberMembership>();
    public DbSet<RoleDefinition> Roles => Set<RoleDefinition>();
    public DbSet<PermissionDefinition> PermissionDefinitions => Set<PermissionDefinition>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyMembership> CompanyMemberships => Set<CompanyMembership>();
    public DbSet<TaxpayerProfile> TaxpayerProfiles => Set<TaxpayerProfile>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<RdoReference> RdoReferences => Set<RdoReference>();
    public DbSet<RdoAssignment> RdoAssignments => Set<RdoAssignment>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<InvitationCompany> InvitationCompanies => Set<InvitationCompany>();
    public DbSet<RegistrationEvidence> RegistrationEvidence => Set<RegistrationEvidence>();

    public DbSet<CompanyEmailSettings> CompanyEmailSettings => Set<CompanyEmailSettings>();
    public DbSet<CompanyTemplate> CompanyTemplates => Set<CompanyTemplate>();
    public DbSet<CompanyTemplateVersion> CompanyTemplateVersions => Set<CompanyTemplateVersion>();

    public DbSet<PlanVersion> PlanVersions => Set<PlanVersion>();
    public DbSet<PackageVersion> PackageVersions => Set<PackageVersion>();
    public DbSet<CheckoutOrder> CheckoutOrders => Set<CheckoutOrder>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<SubscriberSubscription> SubscriberSubscriptions => Set<SubscriberSubscription>();
    public DbSet<BillingPaymentMethod> BillingPaymentMethods => Set<BillingPaymentMethod>();
    public DbSet<AutoTopUpPreference> AutoTopUpPreferences => Set<AutoTopUpPreference>();
    public DbSet<CreditWallet> CreditWallets => Set<CreditWallet>();
    public DbSet<CreditLot> CreditLots => Set<CreditLot>();
    public DbSet<CreditReservation> CreditReservations => Set<CreditReservation>();
    public DbSet<CreditLedgerEntry> CreditLedgerEntries => Set<CreditLedgerEntry>();

    public DbSet<MasterRecord> MasterRecords => Set<MasterRecord>();
    public DbSet<MasterRevision> MasterRevisions => Set<MasterRevision>();
    public DbSet<MasterLink> MasterLinks => Set<MasterLink>();
    public DbSet<ExternalMasterReference> ExternalMasterReferences => Set<ExternalMasterReference>();
    public DbSet<MasterImportOperation> MasterImportOperations => Set<MasterImportOperation>();

    public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();
    public DbSet<FiscalDocumentLine> FiscalDocumentLines => Set<FiscalDocumentLine>();
    public DbSet<FiscalDocumentSnapshot> FiscalDocumentSnapshots => Set<FiscalDocumentSnapshot>();
    public DbSet<DocumentNumberSeries> DocumentNumberSeries => Set<DocumentNumberSeries>();
    public DbSet<DocumentCommitment> DocumentCommitments => Set<DocumentCommitment>();
    public DbSet<DocumentAllocation> DocumentAllocations => Set<DocumentAllocation>();
    public DbSet<DocumentProcessing> DocumentProcessing => Set<DocumentProcessing>();
    public DbSet<DocumentOutboxMessage> DocumentOutboxMessages => Set<DocumentOutboxMessage>();
    public DbSet<IssuedDocumentFile> IssuedDocumentFiles => Set<IssuedDocumentFile>();
    public DbSet<DocumentDeliveryAttempt> DocumentDeliveryAttempts => Set<DocumentDeliveryAttempt>();

    internal DbSet<Connector> Connectors => Set<Connector>();
    internal DbSet<ConnectorVersion> ConnectorVersions => Set<ConnectorVersion>();
    internal DbSet<VendorConnectorGrant> VendorConnectorGrants => Set<VendorConnectorGrant>();
    internal DbSet<ExternalConnection> ExternalConnections => Set<ExternalConnection>();
    internal DbSet<ConnectionCredential> ConnectionCredentials => Set<ConnectionCredential>();
    internal DbSet<ConnectionMapping> ConnectionMappings => Set<ConnectionMapping>();
    internal DbSet<SourceOrder> SourceOrders => Set<SourceOrder>();
    internal DbSet<DesktopAgentRegistration> DesktopAgentRegistrations => Set<DesktopAgentRegistration>();

    internal DbSet<MappingProfile> MappingProfiles => Set<MappingProfile>();
    internal DbSet<MappingVersion> MappingVersions => Set<MappingVersion>();
    internal DbSet<ReferenceMapping> ReferenceMappings => Set<ReferenceMapping>();
    internal DbSet<VendorMappingTemplate> VendorMappingTemplates => Set<VendorMappingTemplate>();
    internal DbSet<ExecutionPolicy> ExecutionPolicies => Set<ExecutionPolicy>();
    internal DbSet<ConnectorTestResult> ConnectorTestResults => Set<ConnectorTestResult>();
    internal DbSet<SyncDestination> SyncDestinations => Set<SyncDestination>();
    internal DbSet<SyncDestinationWindow> SyncDestinationWindows => Set<SyncDestinationWindow>();
    internal DbSet<SyncOperation> SyncOperations => Set<SyncOperation>();
    internal DbSet<SourceOrderObservation> SourceOrderObservations => Set<SourceOrderObservation>();
    internal DbSet<SyncAttempt> SyncAttempts => Set<SyncAttempt>();
    internal DbSet<ExternalDocumentIdentity> ExternalDocumentIdentities => Set<ExternalDocumentIdentity>();
    internal DbSet<MockAccountingRecord> MockAccountingRecords => Set<MockAccountingRecord>();

    internal DbSet<CoverageRevision> CoverageRevisions => Set<CoverageRevision>();
    internal DbSet<AccountingObservation> AccountingObservations => Set<AccountingObservation>();
    internal DbSet<ReconciliationCheck> ReconciliationChecks => Set<ReconciliationCheck>();
    internal DbSet<ReconciliationFinding> ReconciliationFindings => Set<ReconciliationFinding>();
    internal DbSet<ReconciliationReview> ReconciliationReviews => Set<ReconciliationReview>();
    internal DbSet<ReconciliationJob> ReconciliationJobs => Set<ReconciliationJob>();
    internal DbSet<AccountingReference> AccountingReferences => Set<AccountingReference>();
    internal DbSet<AccountingMatch> AccountingMatches => Set<AccountingMatch>();
    internal DbSet<MockAccountingRevision> MockAccountingRevisions => Set<MockAccountingRevision>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        Modules.Reporting.ReportingGateway.Configure(b);
        Modules.Reporting.RegulatorAccess.Configure(b);
        Modules.Reporting.TaxpayerPortability.Configure(b);
        Modules.Reporting.TaxpayerPreservation.Configure(b);
        Modules.Reporting.ReportingContinuity.Configure(b);
        Modules.Reporting.RegulatoryAuthorizations.Configure(b);
        Modules.Reporting.VendorRegulatoryReview.Configure(b);
        Modules.Reporting.RegulatoryConnector.Configure(b);
        Modules.Reporting.ReportingOperations.Configure(b);
        Modules.Reporting.VendorAccreditation.Configure(b);
        Modules.Reporting.RegulatorySchemas.Configure(b);
        CompanyConfigurationModel.Configure(b);
        BillingModel.Configure(b);
        MasterModel.Configure(b);
        DocumentModel.Configure(b);
        ConnectModel.Configure(b);
        MappingModel.Configure(b);
        ExecutionModel.Configure(b);
        SyncModel.Configure(b);
        SourceObservations.Configure(b);
        ReconciliationModel.Configure(b);
        b.Entity<User>().Property(x => x.Kind).HasConversion<string>();
        b.Entity<User>().Property(x => x.Status).HasConversion<string>();
        b.Entity<UserIdentity>().HasKey(x => x.UserId);
        b.Entity<UserIdentity>().HasOne(x => x.User).WithOne(x => x.Identity).HasForeignKey<UserIdentity>(x => x.UserId);
        b.Entity<UserIdentity>().HasIndex(x => x.NormalizedEmail).IsUnique();
        b.Entity<UserIdentity>().Property(x => x.NormalizedEmail).HasMaxLength(254);
        b.Entity<Registration>().HasKey(x => x.UserId);
        b.Entity<Registration>().HasOne<User>().WithOne().HasForeignKey<Registration>(x => x.UserId);
        b.Entity<IdentityToken>().HasKey(x => x.Digest);
        b.Entity<IdentityToken>().Property(x => x.Digest).HasMaxLength(64);
        b.Entity<IdentityToken>().Property(x => x.Purpose).HasConversion<string>();
        b.Entity<IdentityToken>().HasIndex(x => new { x.UserId, x.Purpose });
        b.Entity<IdentityToken>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<UserSession>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<UserSession>().HasIndex(x => new { x.UserId, x.ExpiresAt });
        b.Entity<SecurityAudit>().HasIndex(x => new { x.SubscriberId, x.At });

        b.Entity<Subscriber>().Property(x => x.Status).HasConversion<string>();
        b.Entity<RoleDefinition>().HasKey(x => x.Id);
        b.Entity<PermissionDefinition>().HasKey(x => x.Id);
        b.Entity<RolePermission>().HasKey(x => new { x.RoleId, x.PermissionId });
        b.Entity<RolePermission>().HasOne<RoleDefinition>().WithMany().HasForeignKey(x => x.RoleId);
        b.Entity<RolePermission>().HasOne<PermissionDefinition>().WithMany().HasForeignKey(x => x.PermissionId);
        b.Entity<RoleDefinition>().HasData(Permissions.Roles.Select(x => new RoleDefinition { Id = x.Id, Name = x.Name }));
        b.Entity<PermissionDefinition>().HasData(Permissions.Roles.SelectMany(x => x.Grants).Distinct().Select(x => new PermissionDefinition { Id = x }));
        b.Entity<RolePermission>().HasData(Permissions.Roles.SelectMany(x => x.Grants.Select(g => new RolePermission { RoleId = x.Id, PermissionId = g })));

        b.Entity<SubscriberMembership>().HasKey(x => new { x.SubscriberId, x.UserId });
        b.Entity<SubscriberMembership>().HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId);
        b.Entity<SubscriberMembership>().HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        b.Entity<SubscriberMembership>().HasOne<RoleDefinition>().WithMany().HasForeignKey(x => x.RoleId);
        b.Entity<Company>().HasOne<Subscriber>().WithMany().HasForeignKey(x => x.SubscriberId);
        b.Entity<Company>().HasAlternateKey(x => new { x.SubscriberId, x.Id });
        b.Entity<Company>().HasIndex(x => new { x.SubscriberId, x.Code }).IsUnique();
        b.Entity<Company>().Property(x => x.ConfigurationStatus).HasConversion<string>();
        b.Entity<CompanyMembership>().HasKey(x => new { x.SubscriberId, x.CompanyId, x.UserId });
        b.Entity<CompanyMembership>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        b.Entity<CompanyMembership>().HasOne<SubscriberMembership>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.UserId });
        b.Entity<TaxpayerProfile>().HasKey(x => new { x.SubscriberId, x.CompanyId });
        b.Entity<TaxpayerProfile>().HasOne<Company>().WithOne().HasForeignKey<TaxpayerProfile>(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey<Company>(x => new { x.SubscriberId, x.Id });
        b.Entity<TaxpayerProfile>().Property(x => x.VerificationStatus).HasConversion<string>();
        b.Entity<TaxpayerProfile>().Property(x => x.IssuanceStatus).HasConversion<string>();
        b.Entity<Branch>().HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        b.Entity<Branch>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        b.Entity<Branch>().HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Code }).IsUnique();
        b.Entity<Branch>().HasIndex(x => new { x.SubscriberId, x.CompanyId }).HasFilter("\"Type\" = 'HeadOffice'").IsUnique();

        b.Entity<RdoReference>().HasKey(x => x.Code);
        b.Entity<RdoAssignment>().HasOne<RdoReference>().WithMany().HasForeignKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);
        b.Entity<RdoAssignment>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        b.Entity<RdoAssignment>().HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.BranchId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        b.Entity<RdoAssignment>().HasIndex(x => new { x.SubscriberId, x.CompanyId, x.BranchId, x.EffectiveFrom });
        b.Entity<RdoAssignment>().HasIndex(x => new { x.SubscriberId, x.CompanyId, x.BranchId }).IsUnique().AreNullsDistinct(false)
            .HasFilter("\"EffectiveUntil\" IS NULL");
        b.Entity<RdoAssignment>().ToTable(t => t.HasCheckConstraint("CK_RdoAssignment_Dates", "\"EffectiveUntil\" IS NULL OR \"EffectiveUntil\" > \"EffectiveFrom\""));
        b.Entity<RegistrationEvidence>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });

        b.Entity<Invitation>().HasAlternateKey(x => new { x.SubscriberId, x.Id });
        b.Entity<Invitation>().HasIndex(x => x.Digest).IsUnique();
        b.Entity<Invitation>().HasIndex(x => new { x.SubscriberId, x.NormalizedEmail });
        b.Entity<Invitation>().HasOne<SubscriberMembership>().WithMany().HasForeignKey(x => new { x.SubscriberId, UserId = x.InvitedById });
        b.Entity<Invitation>().HasOne<RoleDefinition>().WithMany().HasForeignKey(x => x.RoleId);
        b.Entity<InvitationCompany>().HasKey(x => new { x.SubscriberId, x.InvitationId, x.CompanyId });
        b.Entity<InvitationCompany>().HasOne<Invitation>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.InvitationId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        b.Entity<InvitationCompany>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
    }
}
