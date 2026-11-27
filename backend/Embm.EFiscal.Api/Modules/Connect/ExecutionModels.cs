using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class ExecutionPolicy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectorVersionId { get; set; }
    public string Capability { get; set; } = "";
    public string Method { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Path { get; set; } = "";
    public string Authentication { get; set; } = "Synthetic Authorization";
    public int TimeoutMs { get; set; } = 3000;
    public int PageLimit { get; set; } = 3;
    public JsonElement? RequestSchema { get; set; }
    public JsonElement? ResponseSchema { get; set; }
    public DateTimeOffset ApprovedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? ApprovedBy { get; set; }
}

internal sealed class ConnectorTestResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PolicyId { get; set; }
    public Guid ActorId { get; set; }
    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;
    public int DurationMs { get; set; }
    public string Status { get; set; } = "";
    public int? HttpStatus { get; set; }
    public JsonElement Diagnostics { get; set; }
}

internal static class ExecutionModel
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<ExecutionPolicy>(e =>
        {
            e.HasOne<ConnectorVersion>().WithMany().HasForeignKey(x => x.ConnectorVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ConnectorVersionId, x.Capability, x.Method, x.Path }).IsUnique();
            e.Property(x => x.RequestSchema).HasColumnType("jsonb");
            e.Property(x => x.ResponseSchema).HasColumnType("jsonb");
        });
        b.Entity<ConnectorTestResult>(e =>
        {
            e.HasOne<ExecutionPolicy>().WithMany().HasForeignKey(x => x.PolicyId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Diagnostics).HasColumnType("jsonb");
        });
    }
}

