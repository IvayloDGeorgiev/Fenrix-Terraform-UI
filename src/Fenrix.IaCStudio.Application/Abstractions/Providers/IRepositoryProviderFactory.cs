using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Abstractions.Providers;

/// <summary>
/// Resolves the right <see cref="IRepositoryProvider"/> adapter for a provider type or a stored repository
/// connection, and builds the transient <see cref="ProviderConnectionContext"/> (resolving the token
/// just-in-time from the secret store). When no adapter matches, callers fall back to generic Git — the
/// factory always returns at least the Generic Git adapter, which advertises
/// <see cref="ProviderCapabilities.None"/>. See docs/09-provider-integrations.md.
/// </summary>
public interface IRepositoryProviderFactory
{
    /// <summary>The adapter for a provider type; Generic Git when none is registered.</summary>
    IRepositoryProvider GetProvider(RepositoryProviderType providerType);

    /// <summary>
    /// Resolves the adapter for a stored repository connection and composes its call context, reading the
    /// access token from the secret store. Returns null when the connection id is unknown.
    /// </summary>
    Task<(IRepositoryProvider Provider, ProviderConnectionContext Context)?> ResolveAsync(
        Guid repositoryConnectionId, CancellationToken ct = default);
}
