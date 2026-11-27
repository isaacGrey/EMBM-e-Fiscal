using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Reporting;

internal sealed class RegulatoryAuthorization
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BranchId { get; set; }
    public required string Kind { get; set; }
    public required string DocumentType { get; set; }
}

internal sealed class RegulatoryAuthorizationVersion
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AuthorizationId { get; set; }
    public int Number { get; set; }
    public required string Reference { get; set; }
    public required string Authority { get; set; }
    public required string Solution { get; set; }
    public required string SolutionVersion { get; set; }
    public required string Scope { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? ExpiresOn { get; set; }
    public required string Filename { get; set; }
    public required string ContentType { get; set; }
    public required string Sha256 { get; set; }
    public int Size { get; set; }
    public Guid SubmittedBy { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
}

internal sealed class RegulatoryAuthorizationFile
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AuthorizationId { get; set; }
    public int Number { get; set; }
    public required byte[] Content { get; set; }
}

internal sealed class RegulatoryAuthorizationReview
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid AuthorizationId { get; set; }
    public int Number { get; set; }
    public int Sequence { get; set; }
    public Guid Reviewer { get; set; }
    public DateTimeOffset ReviewedAt { get; set; }
    public required string Decision { get; set; }
    public required string Reason { get; set; }
}

internal static partial class RegulatoryAuthorizations
{
    internal const string View = "Regulatory.Evidence.View";
    internal const string History = "Regulatory.Evidence.History";
    internal const string Submit = "Regulatory.Evidence.Submit";
    internal const string Review = "Regulatory.Evidence.Review";
    internal const string RequestMore = "Regulatory.Evidence.RequestMore";
    internal const string Reject = "Regulatory.Evidence.Reject";
    internal const string DocumentExternal = "Regulatory.Authorization.RecordExternal";
    internal const string Suspend = "Regulatory.Authorization.Suspend";
    internal const string Revoke = "Regulatory.Authorization.Revoke";
    internal static readonly string[] ReaderGrants = [View, History];
    internal static readonly string[] ReviewerGrants = [View, History, Review, RequestMore, Reject, DocumentExternal, Suspend, Revoke];

    internal static void Configure(ModelBuilder model)
    {
        var register = model.Entity<RegulatoryAuthorization>();
        register.HasKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        register.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.BranchId, x.Kind, x.DocumentType }).IsUnique();
        register.HasOne<TaxpayerProfile>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId }).OnDelete(DeleteBehavior.Restrict);
        register.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.BranchId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        register.ToTable("RegulatoryAuthorizations", t =>
        {
            t.HasCheckConstraint("CK_RegulatoryAuthorization_Document", """ "DocumentType" IN ('SalesInvoice', 'CreditMemo') """);
            t.HasCheckConstraint("CK_RegulatoryAuthorization_Kind", """
                "Kind" IN ('PermitToIssue', 'EisCertification', 'PermitToTransmit', 'CasRegistration', 'PermitToUse',
                  'AcknowledgmentCertificate', 'Other')
                """);
        });

        var version = model.Entity<RegulatoryAuthorizationVersion>();
        version.HasKey(x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number });
        version.HasOne<RegulatoryAuthorization>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.AuthorizationId })
            .OnDelete(DeleteBehavior.Restrict);
        version.ToTable("RegulatoryAuthorizationVersions", t =>
        {
            t.HasCheckConstraint("CK_RegulatoryVersion_Dates", """ "ExpiresOn" IS NULL OR "ExpiresOn" >= "EffectiveFrom" """);
            t.HasCheckConstraint("CK_RegulatoryVersion_Evidence", """ "Number" > 0 AND "Size" BETWEEN 1 AND 2097152 AND length("Sha256") = 64 """);
        });

        var file = model.Entity<RegulatoryAuthorizationFile>();
        file.HasKey(x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number });
        file.HasOne<RegulatoryAuthorizationVersion>().WithOne().HasForeignKey<RegulatoryAuthorizationFile>(
            x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number }).OnDelete(DeleteBehavior.Restrict);
        file.ToTable("RegulatoryAuthorizationFiles", t =>
            t.HasCheckConstraint("CK_RegulatoryFile_Size", """ octet_length("Content") BETWEEN 1 AND 2097152 """));

        var review = model.Entity<RegulatoryAuthorizationReview>();
        review.HasKey(x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number, x.Sequence });
        review.HasOne<RegulatoryAuthorizationVersion>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.AuthorizationId, x.Number })
            .OnDelete(DeleteBehavior.Restrict);
        review.ToTable("RegulatoryAuthorizationReviews", t => t.HasCheckConstraint("CK_RegulatoryReview_Decision",
            """ "Sequence" > 0 AND "Decision" IN ('ReviewAccepted', 'AdditionalEvidence', 'Rejected', 'ExternalAuthorizationDocumented', 'Suspended', 'Revoked') """));
    }
}
