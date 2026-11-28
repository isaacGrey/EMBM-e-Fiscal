using Embm.EFiscal.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Tenants;

internal sealed record TenantGrant(Subscriber Subscriber, string Role, string[] Grants)
{
    public bool IsOwner => Role == Permissions.Owner;
}

internal static class TenantAccess
{
    public static async Task<TenantGrant?> ResolveAsync(EfiscalDbContext db, HttpContext http, Guid subscriber,
        string? permission, CancellationToken ct, bool existingRecords = false)
    {
        var id = http.UserId();
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        var membership = await (
            from member in db.SubscriberMemberships
            join account in db.Subscribers on member.SubscriberId equals account.Id
            where member.SubscriberId == subscriber && member.UserId == id &&
                (account.Status == CommercialStatus.Active || existingRecords && account.Status != CommercialStatus.PendingPayment) && (development || !account.DevelopmentFixture)
            select new { Subscriber = account, Role = member.RoleId }).AsNoTracking().SingleOrDefaultAsync(ct);
        if (membership is null) return null;
        var grants = await db.RolePermissions.Where(x => x.RoleId == membership.Role).Select(x => x.PermissionId).ToArrayAsync(ct);
        return permission is null || grants.Contains(permission) ? new TenantGrant(membership.Subscriber, membership.Role, grants) : null;
    }

    public static IQueryable<Company> Companies(EfiscalDbContext db, Guid subscriber, Guid user, TenantGrant grant) =>
        db.Companies.Where(c => c.SubscriberId == subscriber && (grant.IsOwner ||
            db.CompanyMemberships.Any(x => x.SubscriberId == subscriber && x.CompanyId == c.Id && x.UserId == user)));

    public static async Task<Company?> CompanyAsync(EfiscalDbContext db, HttpContext http, Guid subscriber,
        Guid company, string permission, CancellationToken ct, bool existingRecords = false)
    {
        var grant = await ResolveAsync(db, http, subscriber, permission, ct, existingRecords);
        return grant is null ? null : await Companies(db, subscriber, http.UserId(), grant).SingleOrDefaultAsync(x => x.Id == company, ct);
    }

    public static IResult Missing() => ApiSupport.Problem(404, "The resource is unavailable");
}
