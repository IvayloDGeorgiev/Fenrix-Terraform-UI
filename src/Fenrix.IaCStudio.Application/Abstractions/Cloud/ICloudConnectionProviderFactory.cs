using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Abstractions.Cloud;

/// <summary>
/// Resolves the right <see cref="ICloudConnectionProvider"/> for a provider type or a stored cloud
/// connection, and builds the transient <see cref="CloudConnectionContext"/> (resolving any service-principal
/// secret just-in-time from the secret store). Mirrors <c>IRepositoryProviderFactory</c>. See
/// docs/10-cloud-integrations.md, docs/11-secrets.md.
/// </summary>
public interface ICloudConnectionProviderFactory
{
    /// <summary>The adapter for a provider type; null when the type is not <see cref="CloudProviderType.Unknown"/>-mappable.</summary>
    ICloudConnectionProvider? GetProvider(CloudProviderType providerType);

    /// <summary>
    /// Resolves the adapter for a stored cloud connection and composes its call context, reading any secret
    /// from the secret store. Returns null when the connection id is unknown or no adapter matches.
    /// </summary>
    Task<(ICloudConnectionProvider Provider, CloudConnectionContext Context)?> ResolveAsync(
        Guid cloudConnectionId, CancellationToken ct = default);
}
