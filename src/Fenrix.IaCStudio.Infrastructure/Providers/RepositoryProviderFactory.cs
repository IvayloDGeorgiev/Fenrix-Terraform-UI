using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// Resolves the right <see cref="IRepositoryProvider"/> for a provider type or stored connection, and builds
/// the transient <see cref="ProviderConnectionContext"/> by reading the access token from the secret store
/// just-in-time. Unknown provider types fall back to <see cref="GenericGitProvider"/>. See
/// docs/09-provider-integrations.md, docs/11-secrets.md.
/// </summary>
public sealed class RepositoryProviderFactory(
    IEnumerable<IRepositoryProvider> providers,
    AppDbContext db,
    ISecretStore secrets,
    ILogger<RepositoryProviderFactory> logger) : IRepositoryProviderFactory
{
    private readonly IReadOnlyDictionary<RepositoryProviderType, IRepositoryProvider> _providers =
        BuildMap(providers);
    private readonly AppDbContext _db = db;
    private readonly ISecretStore _secrets = secrets;
    private readonly ILogger<RepositoryProviderFactory> _logger = logger;

    public IRepositoryProvider GetProvider(RepositoryProviderType providerType) =>
        _providers.TryGetValue(providerType, out var provider)
            ? provider
            : _providers[RepositoryProviderType.GenericGit];

    public async Task<(IRepositoryProvider Provider, ProviderConnectionContext Context)?> ResolveAsync(
        Guid repositoryConnectionId, CancellationToken ct = default)
    {
        var connection = await _db.RepositoryConnections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == repositoryConnectionId, ct);
        if (connection is null)
            return null;

        string? token = null;
        string? userName = null;
        if (connection.SecretReferenceId is { } refId)
        {
            var reference = await _db.SecretReferences.AsNoTracking().FirstOrDefaultAsync(r => r.Id == refId, ct);
            if (reference is not null)
            {
                try
                {
                    var raw = await _secrets.RetrieveAsync(reference, ct);
                    if (!string.IsNullOrEmpty(raw))
                    {
                        var credential = RepositoryCredential.Parse(raw);
                        token = credential.Token;
                        userName = credential.UserName;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not resolve the secret for connection {Id}.", repositoryConnectionId);
                }
            }
        }

        var context = new ProviderConnectionContext(
            connection.Id,
            connection.ProviderType,
            connection.DisplayName,
            connection.BaseUrl,
            connection.Organisation,
            connection.ProjectOrWorkspace,
            token,
            userName);

        return (GetProvider(connection.ProviderType), context);
    }

    private static Dictionary<RepositoryProviderType, IRepositoryProvider> BuildMap(
        IEnumerable<IRepositoryProvider> providers)
    {
        var map = new Dictionary<RepositoryProviderType, IRepositoryProvider>();
        foreach (var provider in providers)
            map[provider.ProviderType] = provider; // last registration wins per type

        // Generic Git is the guaranteed fallback; register a default if none was supplied.
        map.TryAdd(RepositoryProviderType.GenericGit, new GenericGitProvider());
        return map;
    }
}
