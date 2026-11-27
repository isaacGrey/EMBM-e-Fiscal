using System.Text.Json;
using Embm.EFiscal.Api.Modules.Tenants;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.MasterData;

public sealed class MasterRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public int Revision { get; set; }
    public JsonElement Data { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class MasterRevision
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public int Revision { get; set; }
    public string Name { get; set; } = "";
    public bool Active { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveThrough { get; set; }
    public JsonElement Data { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset At { get; set; }
    public string Action { get; set; } = "";
}

public sealed class MasterLink
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public string Field { get; set; } = "";
    public Guid TargetId { get; set; }
}

public sealed class ExternalMasterReference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid RecordId { get; set; }
    public string Connector { get; set; } = "";
    public string ExternalAccount { get; set; } = "";
    public string RecordType { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string? ExternalCode { get; set; }
    public DateTimeOffset? SourceAt { get; set; }
}

public sealed class MasterImportOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ActorId { get; set; }
    public string Kind { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public int Rows { get; set; }
}

internal record MasterInput(string? Code, string? Name, bool Active, int? Revision, Dictionary<string, JsonElement>? Data);
internal sealed class MasterValidation(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

internal static class MasterModel
{
    public static void Configure(ModelBuilder b)
    {
        var record = b.Entity<MasterRecord>();
        record.HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        record.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        record.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Kind, x.Code }).IsUnique();
        record.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Kind, x.Active, x.Name });
        record.Property(x => x.Kind).HasMaxLength(32);
        record.Property(x => x.Code).HasMaxLength(40);
        record.Property(x => x.Name).HasMaxLength(160);
        record.Property(x => x.Data).HasColumnType("jsonb");
        record.ToTable(t => t.HasCheckConstraint("CK_MasterRecord_Revision", "\"Revision\" > 0"));

        var revision = b.Entity<MasterRevision>();
        revision.HasKey(x => new { x.SubscriberId, x.CompanyId, x.RecordId, x.Revision });
        revision.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.RecordId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        revision.Property(x => x.Data).HasColumnType("jsonb");
        revision.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.RecordId, x.EffectiveFrom, x.Revision });
        revision.ToTable(t => t.HasCheckConstraint("CK_MasterRevision_Dates", "\"EffectiveThrough\" IS NULL OR \"EffectiveThrough\" >= \"EffectiveFrom\""));

        var link = b.Entity<MasterLink>();
        link.HasKey(x => new { x.SubscriberId, x.CompanyId, x.RecordId, x.Field, x.TargetId });
        link.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.RecordId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        link.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.TargetId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        link.Property(x => x.Field).HasMaxLength(60);

        var external = b.Entity<ExternalMasterReference>();
        external.HasOne<MasterRecord>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.RecordId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        external.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Connector, x.ExternalAccount, x.RecordType, x.ExternalId }).IsUnique();
        foreach (var key in new[] { "Connector", "ExternalAccount", "RecordType", "ExternalId", "ExternalCode" })
            external.Property<string>(key).HasMaxLength(120);

        var import = b.Entity<MasterImportOperation>();
        import.HasOne<Company>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId })
            .HasPrincipalKey(x => new { x.SubscriberId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        import.Property(x => x.Digest).HasMaxLength(64);
        import.HasIndex(x => new { x.SubscriberId, x.CompanyId, x.Kind, x.Digest }).IsUnique();
    }
}

