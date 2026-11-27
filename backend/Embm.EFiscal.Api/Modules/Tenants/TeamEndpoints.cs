using System.Security.Claims;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Billing;
using Embm.EFiscal.Api.Modules.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Tenants;

public sealed record InviteRequest(string Email, string RoleId, Guid[] CompanyIds);
public sealed record RoleRequest(string RoleId, Guid[] CompanyIds);
public sealed record InvitationRegistration(string Token, string FullName, string Mobile, string Password, string Confirmation, bool TermsAccepted, bool PrivacyAccepted);

public static class TeamEndpoints
{
    public static void MapTeam(this WebApplication app)
    {
        var team = app.MapGroup("/api/workspace/{subscriberId:guid}/team").RequireAuthorization("customer").WithTags("Team");
        team.AddEndpointFilter(ApiSupport.Protect);
        team.MapGet("/", Team);
        team.MapPost("/invitations", Invite).RequireRateLimiting("identity");
        team.MapPut("/members/{userId:guid}", Assign);
        team.MapDelete("/members/{userId:guid}", Remove);
        var links = app.MapGroup("/api/invitations").WithTags("Team");
        links.AddEndpointFilter(ApiSupport.Protect);
        links.MapPost("/accept", (TokenRequest input, HttpContext http, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
            Accept(input.Token, null, http, db, hasher, ct)).RequireAuthorization("customer").RequireRateLimiting("identity");
        links.MapPost("/register", (InvitationRegistration input, HttpContext http, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
            Accept(input.Token, input, http, db, hasher, ct)).AllowAnonymous().RequireRateLimiting("identity");
    }

    private static async Task<IResult> Team(Guid subscriberId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var access = await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersInvite, ct);
        if (access is null) return TenantAccess.Missing();
        var userId = http.UserId();
        var invitations = await db.Invitations.Where(i => i.SubscriberId == subscriberId && (access.IsOwner || i.InvitedById == userId))
            .OrderByDescending(x => x.ExpiresAt).Take(100).Select(x => new { x.Id, x.Email, x.RoleId, x.AcceptedAt, x.ExpiresAt }).ToListAsync(ct);
        var members = access.IsOwner ? await (
            from m in db.SubscriberMemberships
            join u in db.Users on m.UserId equals u.Id
            where m.SubscriberId == subscriberId
            orderby u.FullName
            select new { m.UserId, u.FullName, u.Identity.Email, m.RoleId,
                companyIds = db.CompanyMemberships.Where(c => c.SubscriberId == subscriberId && c.UserId == u.Id).Select(c => c.CompanyId).ToArray() })
            .ToListAsync(ct) : [];
        return Results.Ok(new { members, invitations, canManage = access.IsOwner,
            roles = Permissions.Roles.Where(role => Permissions.CanAssign(access.Role, role.Id))
                .Select(role => new { role.Id, role.Name, permissions = role.Grants }) });
    }

    private static async Task<IResult> Invite(Guid subscriberId, InviteRequest input, HttpContext http, EfiscalDbContext db, IIdentityEmail delivery, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersInvite, ct) is null) return TenantAccess.Missing();
        if (!ApiSupport.Email(input.Email)) return ApiSupport.Invalid("email", "Enter a valid email address.");
        if (!delivery.Available) return ApiSupport.Problem(503, "Invitation email delivery is unavailable");
        if (!ValidCompanies(input.CompanyIds)) return ApiSupport.Invalid("companyIds", "Select one or more authorized company files.");
        var companyIds = input.CompanyIds.Distinct().ToArray();
        var actor = http.UserId();
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var access = await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersInvite, ct);
        if (access is null) return TenantAccess.Missing();
        if (!await new CreditEngine(db).AllowsOperations(subscriberId, DateTimeOffset.UtcNow, ct))
            return ApiSupport.Problem(409, "The subscription does not allow new invitations");
        if (!Permissions.CanAssign(access.Role, input.RoleId)) return ApiSupport.Problem(403, "This role cannot be assigned");
        if (await TenantAccess.Companies(db, subscriberId, actor, access).CountAsync(x => companyIds.Contains(x.Id), ct) != companyIds.Length)
            return TenantAccess.Missing();
        var normalized = ApiSupport.NormalizeEmail(input.Email);
        var now = DateTimeOffset.UtcNow;
        if (await db.Invitations.AnyAsync(x => x.SubscriberId == subscriberId && x.NormalizedEmail == normalized && x.AcceptedAt == null && x.ExpiresAt > now, ct))
            return ApiSupport.Problem(409, "An active invitation already exists");
        var exists = await (from m in db.SubscriberMemberships
                    join i in db.UserIdentities on m.UserId equals i.UserId
                    where m.SubscriberId == subscriberId && i.NormalizedEmail == normalized
                    select m.UserId).AnyAsync(ct);
        if (exists) return ApiSupport.Problem(409, "This address already belongs to a subscriber member");
        var occupied = await db.SubscriberMemberships.CountAsync(x => x.SubscriberId == subscriberId, ct);
        var reserved = await db.Invitations.CountAsync(x => x.SubscriberId == subscriberId && x.AcceptedAt == null && x.ExpiresAt > now, ct);
        if (occupied + reserved >= access.Subscriber.UserLimit)
            return ApiSupport.Problem(409, "Subscriber user allowance reached");
        var token = IdentitySecurity.Token();
        var invitation = new Invitation { SubscriberId = subscriberId, InvitedById = actor, Email = input.Email.Trim(),
            NormalizedEmail = normalized, RoleId = input.RoleId, Digest = IdentitySecurity.Digest(token), ExpiresAt = now.AddDays(2) };
        db.Invitations.Add(invitation);
        db.InvitationCompanies.AddRange(companyIds.Select(company => new InvitationCompany { SubscriberId = subscriberId, InvitationId = invitation.Id, CompanyId = company }));
        db.Audit("InvitationCreated", actor, subscriberId, invitation.Id);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await delivery.SendAsync(invitation.Email, "Your eFiscal invitation", "/invitation", token, ct);
        return Results.Accepted(value: new { invitation.Id, invitation.ExpiresAt });
    }

    private static async Task<IResult> Accept(string token, InvitationRegistration? registration, HttpContext http,
        EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct)
    {
        if (!IdentitySecurity.ValidToken(token)) return InvalidLink();
        if (registration is not null)
        {
            if (http.User.Identity?.IsAuthenticated == true) return ApiSupport.Problem(409, "Sign out or accept with the invited account.");
            if (!ApiSupport.Text(registration.FullName, 100)) return ApiSupport.Invalid("fullName", "Enter your full name.");
            if (!IdentityEndpoints.PhoneValid(registration.Mobile)) return ApiSupport.Invalid("mobile", "Enter a Philippine mobile number.");
            if (!IdentitySecurity.ValidPassword(registration.Password) || registration.Password != registration.Confirmation)
                return ApiSupport.Invalid("password", "Use a matching password of 12 to 128 characters.");
            if (!registration.TermsAccepted || !registration.PrivacyAccepted) return ApiSupport.Invalid("termsAccepted", "Acknowledge both terms and privacy.");
        }
        var digest = IdentitySecurity.Digest(token);
        var tenant = await db.Invitations.Where(x => x.Digest == digest).Select(x => (Guid?)x.SubscriberId).SingleOrDefaultAsync(ct);
        if (tenant is not Guid subscriberId) return InvalidLink();

        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var invite = await db.Invitations.SingleOrDefaultAsync(x => x.SubscriberId == subscriberId && x.Digest == digest, ct);
        var subscriber = await db.Subscribers.FindAsync([subscriberId], ct);
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        var now = DateTimeOffset.UtcNow;
        if (invite is null || invite.AcceptedAt is not null || invite.ExpiresAt <= now || subscriber?.Status != CommercialStatus.Active ||
            (!development && subscriber.DevelopmentFixture)) return InvalidLink();
        if (!await new CreditEngine(db).AllowsOperations(subscriberId, now, ct))
            return ApiSupport.Problem(409, "The subscription does not allow invitation acceptance");

        var actorRole = await (from m in db.SubscriberMemberships
                        join u in db.Users on m.UserId equals u.Id
                        where m.SubscriberId == subscriberId && m.UserId == invite.InvitedById && u.Status == IdentityStatus.Verified
                        select m.RoleId).SingleOrDefaultAsync(ct);
        if (actorRole is null || !Permissions.CanAssign(actorRole, invite.RoleId) ||
            !await db.RolePermissions.AnyAsync(x => x.RoleId == actorRole && x.PermissionId == Permissions.UsersInvite, ct)) return InvalidLink();
        var companies = await db.InvitationCompanies.Where(x => x.SubscriberId == subscriberId && x.InvitationId == invite.Id)
            .Select(x => x.CompanyId).ToArrayAsync(ct);
        if (companies.Length == 0 || (actorRole != Permissions.Owner &&
            await db.CompanyMemberships.CountAsync(x => x.SubscriberId == subscriberId && x.UserId == invite.InvitedById && companies.Contains(x.CompanyId), ct) != companies.Length))
            return InvalidLink();
        if (await db.SubscriberMemberships.CountAsync(x => x.SubscriberId == subscriberId, ct) >= subscriber.UserLimit)
            return ApiSupport.Problem(409, "Subscriber user allowance reached");

        Guid userId;
        if (registration is null)
        {
            userId = http.UserId();
            if (!await db.UserIdentities.AnyAsync(x => x.UserId == userId && x.NormalizedEmail == invite.NormalizedEmail, ct)) return InvalidLink();
        }
        else
        {
            if (await db.UserIdentities.AnyAsync(x => x.NormalizedEmail == invite.NormalizedEmail, ct))
                return ApiSupport.Problem(409, "Use the existing sign-in or password recovery to accept this invitation.");
            var user = IdentitySecurity.CreateUser(hasher, invite.Email, registration.FullName, registration.Mobile, registration.Password, verified: true);
            user.DevelopmentFixture = subscriber.DevelopmentFixture;
            userId = user.Id;
            db.Users.Add(user);
            db.Audit("InvitationTermsAndPrivacyAccepted", userId, subscriberId, invite.Id);
        }
        if (await db.SubscriberMemberships.AnyAsync(x => x.SubscriberId == subscriberId && x.UserId == userId, ct)) return InvalidLink();
        invite.AcceptedAt = now;
        db.SubscriberMemberships.Add(new SubscriberMembership { SubscriberId = subscriberId, UserId = userId, RoleId = invite.RoleId, CreatedAt = now });
        db.CompanyMemberships.AddRange(companies.Select(company => new CompanyMembership { SubscriberId = subscriberId, CompanyId = company, UserId = userId }));
        db.Audit("InvitationAccepted", userId, subscriberId, invite.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException error) when (ApiSupport.IsUniqueViolation(error)) { return ApiSupport.Problem(409, "Sign in with the invited account and try again."); }
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Assign(Guid subscriberId, Guid userId, RoleRequest input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersManage, ct) is null) return TenantAccess.Missing();
        if (!ValidCompanies(input.CompanyIds)) return ApiSupport.Invalid("companyIds", "Select one or more company files.");
        var companies = input.CompanyIds.Distinct().ToArray();
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        var access = await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersManage, ct);
        if (access is not { IsOwner: true } || !Permissions.CanAssign(access.Role, input.RoleId)) return ApiSupport.Problem(403, "This role cannot be assigned");
        var membership = await db.SubscriberMemberships.FindAsync([subscriberId, userId], ct);
        if (membership is null) return TenantAccess.Missing();
        if (membership.RoleId == Permissions.Owner) return ApiSupport.Problem(409, "Owner changes require a secure transfer");
        if (await db.Companies.CountAsync(x => x.SubscriberId == subscriberId && companies.Contains(x.Id), ct) != companies.Length)
            return TenantAccess.Missing();
        await db.CompanyMemberships.Where(x => x.SubscriberId == subscriberId && x.UserId == userId).ExecuteDeleteAsync(ct);
        membership.RoleId = input.RoleId;
        db.CompanyMemberships.AddRange(companies.Select(company => new CompanyMembership { SubscriberId = subscriberId, CompanyId = company, UserId = userId }));
        db.Audit("MembershipAssigned", http.UserId(), subscriberId, userId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Remove(Guid subscriberId, Guid userId, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersManage, ct) is null) return TenantAccess.Missing();
        await using var transaction = await db.LockSubscriberAsync(subscriberId, ct);
        if (await TenantAccess.ResolveAsync(db, http, subscriberId, Permissions.UsersManage, ct) is not { IsOwner: true }) return TenantAccess.Missing();
        var membership = await db.SubscriberMemberships.FindAsync([subscriberId, userId], ct);
        if (membership is null) return TenantAccess.Missing();
        if (membership.RoleId == Permissions.Owner) return ApiSupport.Problem(409, "Owner changes require a secure transfer");
        db.Remove(membership);
        db.Audit("MembershipRemoved", http.UserId(), subscriberId, userId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static bool ValidCompanies(Guid[]? values) => values is { Length: > 0 and <= 100 };
    private static IResult InvalidLink() => ApiSupport.Problem(400, "This invitation is invalid, expired, or unavailable");
}
