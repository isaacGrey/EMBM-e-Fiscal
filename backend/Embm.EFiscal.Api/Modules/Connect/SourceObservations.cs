using System.Text.Json;
using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Embm.EFiscal.Api.Modules.Connect.ConnectorRules;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class SourceOrderObservation
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SourceOrderId { get; set; }
    public int Revision { get; set; }
    public JsonElement Snapshot { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal static class SourceObservations
{
    public static void Configure(ModelBuilder b)
    {
        b.Entity<SourceOrder>().HasAlternateKey(x => new { x.SubscriberId, x.CompanyId, x.Id });
        b.Entity<SourceOrderObservation>(e =>
        {
            e.HasKey(x => new { x.SubscriberId, x.CompanyId, x.SourceOrderId, x.Revision });
            e.HasOne<SourceOrder>().WithMany().HasForeignKey(x => new { x.SubscriberId, x.CompanyId, Id = x.SourceOrderId })
                .HasPrincipalKey(x => new { x.SubscriberId, x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Snapshot).HasColumnType("jsonb");
        });
    }

    public static async Task<Dictionary<Guid, SourceOrderObservation>> Latest(EfiscalDbContext db, Guid subscriber, Guid company, Guid[] ids, CancellationToken ct)
    {
        var scope = db.SourceOrderObservations.AsNoTracking().Where(x => x.SubscriberId == subscriber && x.CompanyId == company && ids.Contains(x.SourceOrderId));
        return await scope.Where(x => !scope.Any(other => other.SourceOrderId == x.SourceOrderId && other.Revision > x.Revision))
            .ToDictionaryAsync(x => x.SourceOrderId, ct);
    }

    public static ProviderOrder? Current(SourceOrder order, SourceOrderObservation? update) =>
        (update?.Snapshot ?? order.Data).Deserialize<ProviderOrder>(SpecImport.Json);

    public static ProviderOrder[] Page(IConnectorAdapter adapter, string resource, string? after, DateTimeOffset? changedSince, string? scenario, int size)
    {
        Require(after is null || after.Length == 4 && after.All(char.IsAsciiDigit), "Use the previous store page cursor.");
        Require(scenario is null or "RefundUpdate", "Use an approved source refresh scenario.");
        var rows = adapter.ReadOrders(resource, null);
        if (scenario == "RefundUpdate")
            rows = rows.Select(x => x.Id == "0001" ? x with
            {
                Status = "Partially Refunded", Refund = 100m, Revision = 2,
                Lines = x.Lines.Select(line => line with { InvoiceableQuantity = Math.Max(0, line.InvoiceableQuantity - 1) }).ToArray()
            } : x).ToArray();
        return rows.Where(x => (after is null || string.CompareOrdinal(x.Id, after) > 0) &&
            (!changedSince.HasValue || new DateTimeOffset(x.OrderedOn.AddDays(x.Revision - 1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) > changedSince))
            .OrderBy(x => x.Id).Take(size + 1).ToArray();
    }
}

