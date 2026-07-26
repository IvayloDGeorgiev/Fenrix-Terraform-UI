using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Contracts.Providers;

/// <summary>
/// The resolved, transient context an adapter needs to talk to a host: which provider, the host base URL
/// (for self-managed / self-hosted endpoints), the org/workspace/project scope, and the access token
/// resolved <em>just-in-time</em> from the secret store. The token lives only in memory for the duration of
/// the call and is never persisted — Fenrix stores only a <c>SecretReference</c>. See docs/11-secrets.md,
/// docs/26-connections.md.
/// </summary>
public sealed record ProviderConnectionContext(
    Guid ConnectionId,
    RepositoryProviderType ProviderType,
    string DisplayName,
    string? BaseUrl,
    string? Organisation,
    string? ProjectOrWorkspace,
    string? AccessToken,
    string? UserName)
{
    /// <summary>True when a token was resolved; adapters short-circuit with an auth error when false.</summary>
    public bool HasToken => !string.IsNullOrWhiteSpace(AccessToken);
}
