using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// Resolves the right <see cref="ICloudConnectionProvider"/> for a provider type or a stored cloud
/// connection, and builds the transient <see cref="CloudConnectionContext"/> — reading any service-principal
/// secret from the secret store just-in-time. Mirrors <c>RepositoryProviderFactory</c>. See
/// docs/10-cloud-integrations.md, docs/11-secrets.md.
/// </summary>
public sealed class CloudConnectionProviderFactory(
    IEnumerable<ICloudConnectionProvider> providers,
    AppDbContext db,
    ISecretStore secrets,
    ILogger<CloudConnectionProviderFactory> logger) : ICloudConnectionProviderFactory
{
    private readonly IReadOnlyDictionary<CloudProviderType, ICloudConnectionProvider> _providers =
        providers.GroupBy(p => p.ProviderType).ToDictionary(g => g.Key, g => g.Last());
    private readonly AppDbContext _db = db;
    private readonly ISecretStore _secrets = secrets;
    private readonly ILogger<CloudConnectionProviderFactory> _logger = logger;

    public ICloudConnectionProvider? GetProvider(CloudProviderType providerType) =>
        _providers.TryGetValue(providerType, out var provider) ? provider : null;

    public async Task<(ICloudConnectionProvider Provider, CloudConnectionContext Context)?> ResolveAsync(
        Guid cloudConnectionId, CancellationToken ct = default)
    {
        var connection = await _db.CloudConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == cloudConnectionId, ct);
        if (connection is null)
            return null;

        var provider = GetProvider(connection.ProviderType);
        if (provider is null)
            return null;

        string? secret = null;
        if (connection.SecretReferenceId is { } refId)
        {
            var reference = await _db.SecretReferences.AsNoTracking().FirstOrDefaultAsync(r => r.Id == refId, ct);
            if (reference is not null)
            {
                try { secret = await _secrets.RetrieveAsync(reference, ct); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not resolve secret for cloud connection {Id}.", cloudConnectionId); }
            }
        }

        var context = new CloudConnectionContext(
            connection.Id,
            connection.ProviderType,
            connection.DisplayName,
            connection.TenantOrAccountId,
            connection.SubscriptionOrProjectId,
            connection.Region,
            connection.ProfileName,
            connection.Client,
            ParseMetadata(connection.MetadataJson),
            secret);

        return (provider, context);
    }

    private static IReadOnlyDictionary<string, string> ParseMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.TrimStart().FirstOrDefault() != '{')
            return new Dictionary<string, string>(0);
        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return parsed ?? new Dictionary<string, string>(0);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(0);
        }
    }
}
