using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Domain.Cloud;

/// <summary>
/// A cloud account/subscription/project a Terraform environment can run against.
/// Defined once in the global library and bound per environment. Holds identifying
/// metadata plus a secret reference, never a secret. See docs/26-connections.md.
/// </summary>
public sealed class CloudConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public CloudProviderType ProviderType { get; set; } = CloudProviderType.Unknown;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? ClientId { get; set; }

    // identifying metadata (no secrets); which subset applies depends on ProviderType
    public string? TenantOrAccountId { get; set; }
    public string? SubscriptionOrProjectId { get; set; }
    public string? Region { get; set; }
    public string? ProfileName { get; set; }
    public string? Client { get; set; }
    public string MetadataJson { get; set; } = "{}";

    public Guid? SecretReferenceId { get; set; }

    // organisation at scale
    public List<string> Tags { get; set; } = [];
    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }

    public ConnectionStatus LastStatus { get; set; } = ConnectionStatus.Untested;
    public DateTimeOffset? LastTestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
