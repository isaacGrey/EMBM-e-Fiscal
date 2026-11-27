using System.Text.Json;
using Embm.EFiscal.Api.Modules.Documents;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class MappingProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public ExternalConnection Connection { get; set; } = null!;
    public string Name { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Direction { get; set; } = "";
    public string Operation { get; set; } = "";
    public int? ActiveVersion { get; set; }
    public bool Disabled { get; set; }
}

internal sealed class MappingVersion
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ProfileId { get; set; }
    public int Number { get; set; }
    public MappingProfile Profile { get; set; } = null!;
    public Guid ConnectorVersionId { get; set; }
    public JsonElement Rules { get; set; }
    public JsonElement Validation { get; set; }
    public JsonElement ReferenceSnapshot { get; set; }
    public bool Validated { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid ActorId { get; set; }
}

internal sealed class ReferenceMapping
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public string Kind { get; set; } = "";
    public string LocalKey { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string ExternalCode { get; set; } = "";
    public string ExternalName { get; set; } = "";
}

internal sealed class VendorMappingTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectorVersionId { get; set; }
    public string Name { get; set; } = "";
    public string DocumentType { get; set; } = "";
    public string Direction { get; set; } = "";
    public int Version { get; set; }
    public JsonElement Rules { get; set; }
    public JsonElement Validation { get; set; }
    public Guid PublishedBy { get; set; }
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal static class MappingModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<MappingProfile>(e =>
        {
            e.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.DocumentType, x.Direction, x.Name }).IsUnique();
            e.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<MappingVersion>(e =>
        {
            e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.ProfileId, x.Number });
            e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ProfileId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ConnectorVersion>().WithMany().HasForeignKey(x => x.ConnectorVersionId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Rules).HasColumnType("jsonb");
            e.Property(x => x.Validation).HasColumnType("jsonb");
            e.Property(x => x.ReferenceSnapshot).HasColumnType("jsonb");
        });
        b.Entity<ReferenceMapping>(e =>
        {
            e.HasOne<ExternalConnection>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.ConnectionId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Kind, x.LocalKey }).IsUnique();
            e.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.ConnectionId, x.Kind, x.ExternalId }).IsUnique();
        });
        b.Entity<VendorMappingTemplate>(e =>
        {
            e.HasOne<ConnectorVersion>().WithMany().HasForeignKey(x => x.ConnectorVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ConnectorVersionId, x.Name, x.DocumentType, x.Direction, x.Version }).IsUnique();
            e.Property(x => x.Rules).HasColumnType("jsonb");
            e.Property(x => x.Validation).HasColumnType("jsonb");
        });
    }
}

