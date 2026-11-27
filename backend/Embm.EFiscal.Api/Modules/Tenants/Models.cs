namespace Embm.EFiscal.Api.Modules.Tenants;

public enum CommercialStatus { PendingPayment, Active, Suspended, Expired, Cancelled }
public enum ConfigurationStatus { Incomplete, Configured }
public enum VerificationStatus { NotVerified, PendingVerification, Verified, RequiresReview, Rejected }
public enum IssuanceStatus { Disabled, PendingRequirements, Authorized, Suspended }

public sealed class Subscriber
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string PlanId { get; set; } = "";
    public string Billing { get; set; } = "";
    public CommercialStatus Status { get; set; }
    public int CompanyLimit { get; set; }
    public int UserLimit { get; set; }
    public bool DevelopmentFixture { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class SubscriberMembership
{
    public Guid SubscriberId { get; set; }
    public Guid UserId { get; set; }
    public string RoleId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class RoleDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
public sealed class PermissionDefinition
{
    public string Id { get; set; } = "";
}
public sealed class RolePermission
{
    public string RoleId { get; set; } = "";
    public string PermissionId { get; set; } = "";
}
public sealed class Company
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public string Code { get; set; } = "";
    public string RegisteredName { get; set; } = "";
    public string? TradeName { get; set; }
    public string BusinessClassification { get; set; } = "";
    public string Address { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public ConfigurationStatus ConfigurationStatus { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
public sealed class CompanyMembership
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
}
public sealed class TaxpayerProfile
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string Tin { get; set; } = "";
    public string VatStatus { get; set; } = "";
    public string? RegistrationDetails { get; set; }
    public string? PermitReference { get; set; }
    public VerificationStatus VerificationStatus { get; set; }
    public IssuanceStatus IssuanceStatus { get; set; }
}
public sealed class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string Type { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string? TaxRegistration { get; set; }
    public string? Contact { get; set; }
}
public sealed class RdoReference
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public bool DevelopmentFixture { get; set; }
    public bool Active { get; set; }
}
public sealed class RdoAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveUntil { get; set; }
    public string RegistrationStatus { get; set; } = "NotVerified";
}
public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid InvitedById { get; set; }
    public string Email { get; set; } = "";
    public string NormalizedEmail { get; set; } = "";
    public string RoleId { get; set; } = "";
    public string Digest { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
}
public sealed class InvitationCompany
{
    public Guid SubscriberId { get; set; }
    public Guid InvitationId { get; set; }
    public Guid CompanyId { get; set; }
}
public sealed class RegistrationEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = "";
    public byte[] Content { get; set; } = [];
    public DateTimeOffset UploadedAt { get; set; }
}
