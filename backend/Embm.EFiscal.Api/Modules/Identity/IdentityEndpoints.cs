using System.Text.RegularExpressions;
using Embm.EFiscal.Api.Infrastructure;
using Embm.EFiscal.Api.Modules.Reporting;
using Embm.EFiscal.Api.Modules.Billing;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Embm.EFiscal.Api.Modules.Identity;

public sealed record RegisterRequest(string Email, string FullName, string Mobile, string Classification, string? BusinessName,
    string Password, string Confirmation, bool TermsAccepted, bool PrivacyAccepted, string PlanId, string Billing);
public sealed record EmailRequest(string Email);
public sealed record TokenRequest(string Token);
public sealed record ResetRequest(string Token, string Password, string Confirmation);
public sealed record LoginRequest(string Email, string Password, bool RememberMe);
public sealed record ProfileRequest(string FullName, string Mobile);
public sealed record PasswordRequest(string CurrentPassword, string Password, string Confirmation);

public static partial class IdentityEndpoints
{
    private static readonly User DummyUser = new();
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<User>().HashPassword(DummyUser, IdentitySecurity.Token()));

    public static void MapIdentity(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").WithTags("Identity");
        auth.MapGet("/csrf", (IAntiforgery antiforgery, HttpContext http) =>
        {
            var token = antiforgery.GetAndStoreTokens(http);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { token = token.RequestToken });
        }).AllowAnonymous().RequireRateLimiting("csrf");
        auth.AddEndpointFilter(ApiSupport.Protect);
        auth.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/verify-email", Verify).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/resend-verification", (EmailRequest input, EfiscalDbContext db, IIdentityEmail email, CancellationToken ct) =>
            SendLink(input, TokenPurpose.VerifyEmail, db, email, ct)).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/forgot-password", (EmailRequest input, EfiscalDbContext db, IIdentityEmail email, CancellationToken ct) =>
            SendLink(input, TokenPurpose.ResetPassword, db, email, ct)).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/reset-password", Reset).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/login", (LoginRequest input, HttpContext http, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
            Login(input, IdentityKind.Customer, http, db, hasher, ct)).AllowAnonymous().RequireRateLimiting("identity");
        auth.MapPost("/vendor/login", async (LoginRequest input, HttpContext http, IWebHostEnvironment env, IConfiguration config,
            EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
            !env.IsDevelopment() || !config.GetValue<bool>("Identity:EnableLocalVendorLogin")
                ? ApiSupport.Problem(403, "Vendor sign-in requires configured MFA")
                : await Login(input, IdentityKind.Vendor, http, db, hasher, ct)).AllowAnonymous().RequireRateLimiting("identity");
        var localRegulator = RegulatorAccess.Enabled(app.Environment, app.Configuration);
        app.MapGet("/api/auth/bir/availability", () => Results.Ok(new { enabled = localRegulator })).AllowAnonymous().WithTags("Identity");
        if (localRegulator)
            auth.MapPost("/bir/login", (LoginRequest input, HttpContext http, EfiscalDbContext db,
                IPasswordHasher<User> hasher, CancellationToken ct) =>
                Login(input, IdentityKind.Regulator, http, db, hasher, ct)).AllowAnonymous().RequireRateLimiting("identity");
        else
            app.MapPost("/api/auth/bir/login", () => ApiSupport.Problem(403, "BIR access is disabled")).AllowAnonymous().WithTags("Identity");
        auth.MapGet("/me", Me);
        auth.MapPost("/logout", Logout);
        auth.MapPut("/profile", UpdateProfile).RequireAuthorization("customer");
        auth.MapPut("/password", ChangePassword).RequireAuthorization("customer").RequireRateLimiting("identity");
    }

    private static async Task<IResult> Register(RegisterRequest input, EfiscalDbContext db, IPasswordHasher<User> hasher, IIdentityEmail email, CancellationToken ct)
    {
        if (!ApiSupport.Email(input.Email)) return ApiSupport.Invalid("email", "Enter a valid email address.");
        if (!ApiSupport.Text(input.FullName, 100)) return ApiSupport.Invalid("fullName", "Enter the account owner's full name.");
        if (!PhoneValid(input.Mobile)) return ApiSupport.Invalid("mobile", "Enter a Philippine mobile number.");
        if (input.Classification is not ("b2b" or "b2c")) return ApiSupport.Invalid("classification", "Choose a subscriber classification.");
        if (input.Classification == "b2b" && !ApiSupport.Text(input.BusinessName, 120)) return ApiSupport.Invalid("businessName", "Enter the business name.");
        if (!IdentitySecurity.ValidPassword(input.Password) || input.Password != input.Confirmation)
            return ApiSupport.Invalid("password", "Use a matching password of 12 to 128 characters.");
        if (!input.TermsAccepted || !input.PrivacyAccepted) return ApiSupport.Invalid("termsAccepted", "Acknowledge both terms and privacy.");
        if (input.Billing is not ("monthly" or "annual") || !await BillingCatalog.PublishedPlans(db, DateTimeOffset.UtcNow).AnyAsync(x => x.Code == input.PlanId, ct))
            return ApiSupport.Invalid("planId", "Select an available subscription option.");
        if (!email.Available) return ApiSupport.Problem(503, "Registration email delivery is unavailable");

        var normalized = ApiSupport.NormalizeEmail(input.Email);
        var user = IdentitySecurity.CreateUser(hasher, input.Email, input.FullName, input.Mobile, input.Password);
        if (await db.UserIdentities.AnyAsync(x => x.NormalizedEmail == normalized, ct)) return Accepted();
        db.Users.Add(user);
        db.Registrations.Add(new Registration { UserId = user.Id, PlanId = input.PlanId, Billing = input.Billing,
            Classification = input.Classification, BusinessName = input.Classification == "b2b" ? input.BusinessName!.Trim() : null,
            TermsAcceptedAt = user.CreatedAt, PrivacyAcceptedAt = user.CreatedAt });
        var token = AddToken(db, user.Id, TokenPurpose.VerifyEmail);
        db.Audit("IdentityRegistered", user.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ApiSupport.IsUniqueViolation(ex)) { return Accepted(); }
        await Deliver(email, TokenPurpose.VerifyEmail, user.Identity.Email, token, ct);
        return Accepted();
    }

    private static async Task<IResult> SendLink(EmailRequest input, TokenPurpose purpose, EfiscalDbContext db, IIdentityEmail email, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return await SendLinkCore(input, purpose, db, email, ct); }
        finally
        {
            var remaining = TimeSpan.FromMilliseconds(250) - System.Diagnostics.Stopwatch.GetElapsedTime(started);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, ct);
        }
    }

    private static async Task<IResult> SendLinkCore(EmailRequest input, TokenPurpose purpose, EfiscalDbContext db, IIdentityEmail email, CancellationToken ct)
    {
        if (!email.Available || !ApiSupport.Email(input.Email)) return Accepted();
        var normalized = ApiSupport.NormalizeEmail(input.Email);
        var id = await db.UserIdentities.Where(x => x.NormalizedEmail == normalized && x.User.Kind == IdentityKind.Customer)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (id is null) return Accepted();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = await IdentitySecurity.LockAsync(db, id.Value, ct);
        var desiredStatus = purpose == TokenPurpose.VerifyEmail ? IdentityStatus.PendingEmailVerification : IdentityStatus.Verified;
        if (identity?.User.Status != desiredStatus) return Accepted();
        var now = DateTimeOffset.UtcNow;
        await db.IdentityTokens.Where(x => x.UserId == id && x.Purpose == purpose && x.UsedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), ct);
        var token = AddToken(db, identity.UserId, purpose);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await Deliver(email, purpose, identity.Email, token, ct);
        return Accepted();
    }

    private static Task<IResult> Verify(TokenRequest input, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        ConsumeToken(input.Token, TokenPurpose.VerifyEmail, null, db, hasher, ct);

    private static Task<IResult> Reset(ResetRequest input, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct) =>
        !IdentitySecurity.ValidPassword(input.Password) || input.Password != input.Confirmation
            ? Task.FromResult(ApiSupport.Invalid("password", "Use a matching password of 12 to 128 characters."))
            : ConsumeToken(input.Token, TokenPurpose.ResetPassword, input.Password, db, hasher, ct);

    private static async Task<IResult> ConsumeToken(string token, TokenPurpose purpose, string? password, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct)
    {
        if (!IdentitySecurity.ValidToken(token)) return ApiSupport.Problem(400, "This link is invalid or expired");
        var hash = IdentitySecurity.Digest(token);
        var id = await db.IdentityTokens.Where(x => x.Digest == hash && x.Purpose == purpose).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (id is null) return ApiSupport.Problem(400, "This link is invalid or expired");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = await IdentitySecurity.LockAsync(db, id.Value, ct);
        var now = DateTimeOffset.UtcNow;
        var link = await db.IdentityTokens.SingleAsync(x => x.Digest == hash, ct);
        var expected = purpose == TokenPurpose.VerifyEmail ? IdentityStatus.PendingEmailVerification : IdentityStatus.Verified;
        if (identity?.User.Status != expected || link.UsedAt is not null || link.ExpiresAt <= now)
            return ApiSupport.Problem(400, "This link is invalid or expired");

        link.UsedAt = now;
        if (purpose == TokenPurpose.VerifyEmail)
        {
            identity.User.Status = IdentityStatus.Verified;
            identity.User.VerifiedAt = now;
            db.Audit("EmailVerified", identity.UserId);
        }
        else
        {
            await RotatePassword(db, hasher, identity, password!, now, ct);
            db.Audit("PasswordReset", identity.UserId);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Login(LoginRequest input, IdentityKind kind, HttpContext http, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct)
    {
        if (!ApiSupport.Email(input.Email) || input.Password is not { Length: > 0 and <= 128 }) return LoginFailed();
        var normalized = ApiSupport.NormalizeEmail(input.Email);
        var id = await db.UserIdentities.Where(x => x.NormalizedEmail == normalized && x.User.Kind == kind)
            .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
        if (id is null)
        {
            hasher.VerifyHashedPassword(DummyUser, DummyHash.Value, input.Password);
            return LoginFailed();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = (await IdentitySecurity.LockAsync(db, id.Value, ct))!;
        var now = DateTimeOffset.UtcNow;
        var development = http.RequestServices.GetRequiredService<IWebHostEnvironment>().IsDevelopment();
        if ((!development && identity.User.DevelopmentFixture) || (kind == IdentityKind.Regulator && !identity.User.DevelopmentFixture) ||
            identity.LockedUntil > now ||
            identity.User.Status is IdentityStatus.Disabled or IdentityStatus.Suspended) return LoginFailed();
        var password = hasher.VerifyHashedPassword(identity.User, identity.PasswordHash, input.Password);
        if (password == PasswordVerificationResult.Failed)
        {
            identity.FailedAttempts = identity.LockedUntil is not null && identity.LockedUntil <= now ? 1 : identity.FailedAttempts + 1;
            identity.LockedUntil = identity.FailedAttempts >= 5 ? now.AddMinutes(15) : null;
            if (identity.LockedUntil is not null) db.Audit("IdentityLocked", identity.UserId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return LoginFailed();
        }
        if (identity.User.Status != IdentityStatus.Verified) return ApiSupport.Problem(403, "Verify your email before signing in");
        identity.FailedAttempts = 0;
        identity.LockedUntil = null;
        if (password == PasswordVerificationResult.SuccessRehashNeeded)
            identity.PasswordHash = IdentitySecurity.Hash(hasher, identity.User, input.Password);
        if (http.User.Identity?.IsAuthenticated == true)
        {
            var prior = http.SessionId();
            await db.Sessions.Where(session => session.Id == prior && session.RevokedAt == null)
                .ExecuteUpdateAsync(update => update.SetProperty(session => session.RevokedAt, now), ct);
        }
        var remember = input.RememberMe && kind == IdentityKind.Customer;
        var session = await IdentitySecurity.IssueSessionAsync(db, http, identity, remember, ct);
        await transaction.CommitAsync(ct);
        await IdentitySecurity.SetCookieAsync(http, identity, session, remember);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var id = http.UserId();
        var user = await db.Users.Include(x => x.Identity).SingleAsync(x => x.Id == id, ct);
        var intent = await db.Registrations.FindAsync([id], ct);
        var hasMembership = await db.SubscriberMemberships.AnyAsync(x => x.UserId == id, ct);
        return Results.Ok(new { user.Id, user.FullName, user.Mobile, user.Identity.Email, kind = user.Kind.ToString(),
            status = user.Status.ToString(), pending = intent is not null && !hasMembership
                ? new { intent.PlanId, intent.Billing, intent.Classification, status = "PendingPayment" } : null });
    }

    private static async Task<IResult> Logout(HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        var id = http.SessionId();
        var now = DateTimeOffset.UtcNow;
        await db.Sessions.Where(x => x.Id == id && x.RevokedAt == null).ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), ct);
        db.Audit("SessionRevoked", http.UserId());
        await db.SaveChangesAsync(ct);
        await http.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> UpdateProfile(ProfileRequest input, HttpContext http, EfiscalDbContext db, CancellationToken ct)
    {
        if (!ApiSupport.Text(input.FullName, 100)) return ApiSupport.Invalid("fullName", "Enter a full name.");
        if (!PhoneValid(input.Mobile)) return ApiSupport.Invalid("mobile", "Enter a Philippine mobile number.");
        var user = (await db.Users.FindAsync([http.UserId()], ct))!;
        user.FullName = input.FullName.Trim();
        user.Mobile = input.Mobile.Trim();
        db.Audit("UserProfileUpdated", user.Id);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(PasswordRequest input, HttpContext http, EfiscalDbContext db, IPasswordHasher<User> hasher, CancellationToken ct)
    {
        if (!IdentitySecurity.ValidPassword(input.Password) || input.Password != input.Confirmation)
            return ApiSupport.Invalid("password", "Use a matching password of 12 to 128 characters.");
        if (input.CurrentPassword is not { Length: > 0 and <= 128 }) return ApiSupport.Invalid("currentPassword", "Enter your current password.");
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var identity = (await IdentitySecurity.LockAsync(db, http.UserId(), ct))!;
        if (hasher.VerifyHashedPassword(identity.User, identity.PasswordHash, input.CurrentPassword) == PasswordVerificationResult.Failed)
            return ApiSupport.Invalid("currentPassword", "The current password did not match.");
        await RotatePassword(db, hasher, identity, input.Password, DateTimeOffset.UtcNow, ct);
        db.Audit("PasswordChanged", identity.UserId);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await http.SignOutAsync();
        return Results.NoContent();
    }

    private static async Task RotatePassword(EfiscalDbContext db, IPasswordHasher<User> hasher, UserIdentity identity, string password, DateTimeOffset now, CancellationToken ct)
    {
        identity.PasswordHash = IdentitySecurity.Hash(hasher, identity.User, password);
        identity.SecurityStamp = Guid.NewGuid();
        identity.FailedAttempts = 0;
        identity.LockedUntil = null;
        await db.Sessions.Where(x => x.UserId == identity.UserId && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), ct);
        await db.IdentityTokens.Where(x => x.UserId == identity.UserId && x.Purpose == TokenPurpose.ResetPassword && x.UsedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.UsedAt, now), ct);
    }

    private static string AddToken(EfiscalDbContext db, Guid user, TokenPurpose purpose)
    {
        var token = IdentitySecurity.Token();
        var expiry = purpose == TokenPurpose.VerifyEmail ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(30);
        db.IdentityTokens.Add(new IdentityToken { UserId = user, Digest = IdentitySecurity.Digest(token), Purpose = purpose, ExpiresAt = DateTimeOffset.UtcNow.Add(expiry) });
        return token;
    }
    private static Task Deliver(IIdentityEmail email, TokenPurpose purpose, string recipient, string token, CancellationToken ct) =>
        purpose == TokenPurpose.VerifyEmail ? email.SendAsync(recipient, "Verify your email", "/verify-email", token, ct)
            : email.SendAsync(recipient, "Reset your password", "/reset-password", token, ct);

    private static IResult Accepted() => Results.Accepted(value: new { message = "If eligible, an email will arrive shortly." });
    private static IResult LoginFailed() => ApiSupport.Problem(401, "Sign-in failed");
    internal static bool PhoneValid(string? value) => value is not null && MobilePattern().IsMatch(value.Trim());
    [GeneratedRegex(@"^(?:\+63|0)9[0-9]{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex MobilePattern();
}
