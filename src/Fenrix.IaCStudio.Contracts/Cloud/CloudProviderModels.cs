using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Contracts.Cloud;

/// <summary>
/// The resolved, transient context a cloud adapter needs to authenticate a Terraform run: the connection's
/// identifying metadata plus, for service-principal auth, the client secret resolved <em>just-in-time</em>
/// from the OS secret store. The secret lives only in memory for the duration of the call and is never
/// persisted, logged, or shown — Fenrix stores only a <c>SecretReference</c>. Mirrors the repository side's
/// <c>ProviderConnectionContext</c>. See docs/10-cloud-integrations.md, docs/11-secrets.md, docs/26-connections.md.
/// </summary>
public sealed record CloudConnectionContext(
    Guid ConnectionId,
    CloudProviderType ProviderType,
    string DisplayName,
    string? TenantOrAccountId,
    string? SubscriptionOrProjectId,
    string? Region,
    string? ProfileName,
    string? ServicePrincipalClientId,
    IReadOnlyDictionary<string, string> Metadata,
    string? Secret)
{
    /// <summary>True when a service-principal secret was resolved (composes credential env vars).</summary>
    public bool HasSecret => !string.IsNullOrWhiteSpace(Secret);

    /// <summary>Convenience: a metadata value or null.</summary>
    public string? MetadataValue(string key) =>
        Metadata.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>
/// A selectable scope discovered from a signed-in cloud CLI: an Azure subscription, an AWS named profile,
/// or a Google project. Powers the "pick a subscription / profile / project" step in the connection dialog.
/// </summary>
public sealed record CloudScope(string Id, string Name, string? Detail = null, bool IsDefault = false);

/// <summary>The identity behind a cloud connection, returned by a successful test (the "Test connection" call).</summary>
public sealed record CloudIdentity(string Account, string? DisplayName = null, string? Detail = null);

/// <summary>
/// The outcome of composing an environment's cloud credentials for execution: whether a connection is
/// bound, the process-scoped environment variables to inject, and a non-secret identity label for the
/// command-preview context chip (e.g. <c>azure:sub-123/eastus</c>). Never carries a secret value in the
/// label. See docs/25-execution-lifecycle.md, docs/23-command-transparency.md.
/// </summary>
public sealed record CloudEnvironmentResult(
    bool HasConnection,
    Guid? ConnectionId,
    string? ConnectionDisplayName,
    string? IdentityChip,
    IReadOnlyDictionary<string, string> EnvironmentVariables)
{
    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>(0);

    /// <summary>No cloud connection is bound to the environment.</summary>
    public static CloudEnvironmentResult None { get; } = new(false, null, null, null, Empty);
}
