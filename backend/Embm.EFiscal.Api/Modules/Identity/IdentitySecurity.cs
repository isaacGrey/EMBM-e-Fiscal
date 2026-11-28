using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reporting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Identity;

internal static class IdentitySecurity
{
    public static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Digest(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static bool ValidToken(string? token) => token is { Length: 64 } && token.All(Uri.IsHexDigit);
    public static bool ValidPassword(string? password) => password is { Length: >= 12 and <= 128 };
    public static string Hash(IPasswordHasher<User> hasher, User user, string password) => hasher.HashPassword(user, password);

    public static User CreateUser(IPasswordHasher<User> hasher, string email, string name, string mobile, string password, bool verified = false)
    {
        var now = DateTimeOffset.UtcNow;
        var user = new User { FullName = name.Trim(), Mobile = mobile.Trim(), CreatedAt = now,
            Status = verified ? IdentityStatus.Verified : IdentityStatus.PendingEmailVerification, VerifiedAt = verified ? now : null };
        user.Identity = new UserIdentity { UserId = user.Id, Email = email.Trim(), NormalizedEmail = ApiSupport.NormalizeEmail(email),
            PasswordHash = Hash(hasher, user, password) };
        return user;
    }

    public static async Task<UserIdentity?> LockAsync(EfiscalDbContext db, Guid id, CancellationToken ct)
    {
        var identities = await db.UserIdentities.FromSqlInterpolated(
            $"SELECT * FROM \"UserIdentities\" WHERE \"UserId\" = {id} FOR UPDATE").ToListAsync(ct);
        var identity = identities.SingleOrDefault();
        if (identity is not null) await db.Entry(identity).Reference(x => x.User).LoadAsync(ct);
        return identity;
    }

    public static async Task<UserSession> IssueSessionAsync(EfiscalDbContext db, HttpContext http, UserIdentity identity, bool remember, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var session = new UserSession { UserId = identity.UserId, SecurityStamp = identity.SecurityStamp, CreatedAt = now,
            ExpiresAt = now.Add(remember ? TimeSpan.FromDays(7) : TimeSpan.FromHours(8)) };
        db.Sessions.Add(session);
        db.Audit("SessionStarted", identity.UserId);
        await db.SaveChangesAsync(ct);
        return session;
    }

    public static async Task SetCookieAsync(HttpContext http, UserIdentity identity, UserSession session, bool remember)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, identity.UserId.ToString()),
            new Claim("sid", session.Id.ToString()), new Claim("identity_kind", identity.User.Kind.ToString()) };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await http.SignInAsync(principal, new AuthenticationProperties { IsPersistent = remember, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
    }

    public static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<EfiscalDbContext>();
        var environment = http.RequestServices.GetRequiredService<IWebHostEnvironment>();
        if (!string.IsNullOrWhiteSpace(http.RequestServices.GetRequiredService<IConfiguration>().GetConnectionString("Efiscal")) &&
            Guid.TryParse(context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) &&
            Guid.TryParse(context.Principal?.FindFirstValue("sid"), out var sid) &&
            Enum.TryParse<IdentityKind>(context.Principal?.FindFirstValue("identity_kind"), out var kind))
        {
            var now = DateTimeOffset.UtcNow;
            var development = environment.IsDevelopment();
            var configuration = http.RequestServices.GetRequiredService<IConfiguration>();
            var vendorEnabled = development && configuration.GetValue<bool>("Identity:EnableLocalVendorLogin");
            var regulatorEnabled = RegulatorAccess.Enabled(environment, configuration);
            var valid = await (from session in db.Sessions
                               join identity in db.UserIdentities on session.UserId equals identity.UserId
                               where session.Id == sid && session.UserId == id && session.RevokedAt == null && session.ExpiresAt > now
                                   && session.SecurityStamp == identity.SecurityStamp && identity.User.Status == IdentityStatus.Verified
                                   && identity.User.Kind == kind
                                   && (development || !identity.User.DevelopmentFixture)
                                   && (identity.User.Kind == IdentityKind.Customer || identity.User.Kind == IdentityKind.Vendor && vendorEnabled ||
                                       identity.User.Kind == IdentityKind.Regulator && identity.User.DevelopmentFixture && regulatorEnabled)
                               select session.Id).AnyAsync(http.RequestAborted);
            if (valid) return;
        }
        context.RejectPrincipal();
        await http.SignOutAsync();
    }
}
