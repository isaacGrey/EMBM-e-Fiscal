using System.Text.Json.Serialization;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.CompanyConfiguration;

public sealed class CompanyEmailSettings
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public int Revision { get; set; }
    public string ProfileJson { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CompanyTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsDefault { get; set; }
    public int CurrentVersion { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CompanyTemplateVersion
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid TemplateId { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public string ContentJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SenderProfile(string SenderName, string SenderEmail, string? ReplyTo, string Provider,
    string? SmtpHost, int? SmtpPort, string? TlsMode, string? Authentication, string[] Cc, string[] Bcc);
public sealed record SenderUpdate(int ExpectedRevision, SenderProfile Profile);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record EmailTemplate(string Subject, string Body, string? Brand, string? Signature, string? Footer, bool IncludeConfiguredCopies);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PdfLayout(string Title, string? Header, string BusinessLayout, string CustomerLayout, string[] Columns,
    string Font, string AccentColor, int MarginMm, string? Logo, string? PaymentInstructions, string? Terms, string? Footer,
    bool SignatureField, bool PageNumbers, string PageSize = "A4", int FontSize = 10, int[]? ColumnWeights = null,
    string SummaryPosition = "right");

public sealed record TemplateInput(string Name, EmailTemplate? Email, PdfLayout? Layout, int ExpectedVersion = 0);
public sealed record ChangeVersion(int Version, int ExpectedVersion);
public sealed record DuplicateTemplate(string Name);
public sealed record PreviewInput(EmailTemplate Email);

internal static class CompanyConfigurationModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<CompanyEmailSettings>().HasKey(x => new { x.SubscriberId, x.CompanyId });
        model.Entity<CompanyEmailSettings>().HasOne<Company>().WithOne().HasForeignKey<CompanyEmailSettings>(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey<Company>(x => new { x.SubscriberId, x.Id });
        model.Entity<CompanyEmailSettings>().Property(x => x.ProfileJson).HasColumnType("jsonb");

        model.Entity<CompanyTemplate>().HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        model.Entity<CompanyTemplate>().HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id });
        model.Entity<CompanyTemplate>().HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Kind, x.DocumentType }).IsUnique()
            .HasFilter("\"IsDefault\"");
        model.Entity<CompanyTemplate>().Property(x => x.Name).HasMaxLength(100);
        model.Entity<CompanyTemplateVersion>().HasKey(x => new { x.SubscriberId, x.CompanyId, x.TemplateId, x.Version });
        model.Entity<CompanyTemplateVersion>().HasOne<CompanyTemplate>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, x.TemplateId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        model.Entity<CompanyTemplateVersion>().Property(x => x.Name).HasMaxLength(100);
        model.Entity<CompanyTemplateVersion>().Property(x => x.ContentJson).HasColumnType("jsonb");
    }
}
