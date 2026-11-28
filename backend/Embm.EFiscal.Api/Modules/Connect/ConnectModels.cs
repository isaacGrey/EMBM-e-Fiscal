using System.Text.Json;

namespace Embm.EFiscal.Api.Modules.Connect;

internal sealed class Connector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = "";
    public bool Disabled { get; set; }
    public string? Lifecycle { get; set; }
}

internal sealed class ConnectorVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConnectorId { get; set; }
    public Connector Connector { get; set; } = null!;
    public int Number { get; set; }
    public int Revision { get; set; } = 1;
    public string State { get; set; } = "Draft Configuration";
    public string Adapter { get; set; } = "review-only";
    public string Category { get; set; } = "";
    public string Availability { get; set; } = "In Development";
    public JsonElement Configuration { get; set; }
    public JsonElement? Specification { get; set; }
    public JsonElement? Validation { get; set; }
    public Guid? ActorId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAt { get; set; }
    public ConnectorVersionRevocation? Revocation { get; set; }
}

internal sealed class ConnectorVersionRevocation
{
    public Guid VersionId { get; init; }
    public Guid ActorId { get; init; }
    public required string Reason { get; init; }
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed class VendorConnectorGrant
{
    public Guid UserId { get; set; }
    public string Permission { get; set; } = "";
}

internal sealed class ExternalConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid ConnectorId { get; set; }
    public Guid ConnectorVersionId { get; set; }
    public ConnectorVersion Version { get; set; } = null!;
    public string Name { get; set; } = "";
    public string ExternalAccountId { get; set; } = "";
    public string ExternalResourceId { get; set; } = "";
    public string ExternalResourceName { get; set; } = "";
    public string ResourceType { get; set; } = "";
    public Guid CredentialReference { get; set; } = Guid.NewGuid();
    public string Status { get; set; } = "Configuration Required";
    public string Authorization { get; set; } = "Authorized";
    public string MappingStatus { get; set; } = "Required";
    public int? MappingVersion { get; set; }
    public DateTimeOffset? LastSuccessfulTest { get; set; }
    public string? LastError { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class ConnectionCredential
{
    public Guid Id { get; set; }
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string ProtectedValue { get; set; } = "";
    public int Revision { get; set; } = 1;
    public DateTimeOffset? RevokedAt { get; set; }
}

internal sealed class ConnectionMapping
{
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public int Number { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ItemId { get; set; }
    public Guid UnitId { get; set; }
    public Guid? ShippingItemId { get; set; }
    public Guid? SellerDiscountGroupId { get; set; }
    public JsonElement FieldMetadata { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class SourceOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid ConnectionId { get; set; }
    public ExternalConnection Connection { get; set; } = null!;
    public string ExternalOrderId { get; set; } = "";
    public string Reference { get; set; } = "";
    public string Status { get; set; } = "";
    public DateOnly Date { get; set; }
    public string Currency { get; set; } = "PHP";
    public JsonElement Data { get; set; }
    public DateTimeOffset RetrievedAt { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class DesktopAgentRegistration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SubscriberId { get; set; }
    public Guid CompanyId { get; set; }
    public string PublicKeyFingerprint { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public JsonElement InstalledAdapters { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastHeartbeatAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

