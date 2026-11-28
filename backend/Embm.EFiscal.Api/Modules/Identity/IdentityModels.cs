namespace Embm.EFiscal.Api.Modules.Identity;

public enum IdentityStatus { PendingEmailVerification, Verified, Suspended, Disabled }
public enum IdentityKind { Customer, Vendor, Regulator }
public enum TokenPurpose { VerifyEmail, ResetPassword }

public sealed class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FullName { get; set; } = "";
    public string Mobile { get; set; } = "";
    public IdentityKind Kind { get; set; }
    public bool DevelopmentFixture { get; set; }
    public IdentityStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public UserIdentity Identity { get; set; } = null!;
}
public sealed class UserIdentity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Guid SecurityStamp { get; set; } = Guid.NewGuid();
    public int FailedAttempts { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}
public sealed class Registration
{
    public Guid UserId { get; set; }
    public string Classification { get; set; } = "";
    public string? BusinessName { get; set; }
    public string PlanId { get; set; } = "";
    public string Billing { get; set; } = "";
    public DateTimeOffset TermsAcceptedAt { get; set; }
    public DateTimeOffset PrivacyAcceptedAt { get; set; }
}
public sealed class IdentityToken
{
    public string Digest { get; set; } = "";
    public Guid UserId { get; set; }
    public TokenPurpose Purpose { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SecurityStamp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ActiveSubscriberId { get; set; }
    public Guid? ActiveCompanyId { get; set; }
    public Guid? ActiveBranchId { get; set; }
}
public sealed class SecurityAudit
{
    public long Id { get; set; }
    public Guid? ActorId { get; set; }
    public Guid? SubscriberId { get; set; }
    public Guid? TargetId { get; set; }
    public string Action { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
