using Fenrix.IaCStudio.Application.Abstractions.Connections;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Contracts.Connections;
using Fenrix.IaCStudio.Domain.Cloud;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Security;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Connections;

/// <summary>
/// EF-backed implementation of the global Connections library. CRUD over cloud and repository connections is
/// searchable and paged for scale; secret values are written to the OS store via <see cref="ISecretStore"/>
/// so only a <see cref="SecretReference"/> is persisted. "Test" for a repository connection calls the
/// provider adapter through <see cref="IRepositoryProviderFactory"/>. See docs/26-connections.md,
/// docs/11-secrets.md.
/// </summary>
public sealed class ConnectionService(
    AppDbContext db,
    ISecretStore secrets,
    IRepositoryProviderFactory providerFactory,
    Fenrix.IaCStudio.Application.Abstractions.Cloud.ICloudConnectionProviderFactory cloudFactory,
    ILogger<ConnectionService> logger) : IConnectionService
{
    private readonly AppDbContext _db = db;
    private readonly ISecretStore _secrets = secrets;
    private readonly IRepositoryProviderFactory _providerFactory = providerFactory;
    private readonly Fenrix.IaCStudio.Application.Abstractions.Cloud.ICloudConnectionProviderFactory _cloudFactory = cloudFactory;
    private readonly ILogger<ConnectionService> _logger = logger;

    // ---- repository connections ----

    public async Task<IReadOnlyList<RepositoryConnection>> GetRepositoryConnectionsAsync(
        ConnectionFilter filter, CancellationToken ct = default)
    {
        var query = ApplyRepositoryFilter(_db.RepositoryConnections.AsNoTracking(), filter);
        var rows = await query
            .OrderByDescending(c => c.IsFavorite)
            .ThenBy(c => c.DisplayName)
            .ToListAsync(ct);
        rows = ApplyTagFilter(rows, filter, c => c.Tags);
        return Page(rows, filter);
    }

    public async Task<int> CountRepositoryConnectionsAsync(ConnectionFilter filter, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(filter.Tag))
            return ApplyTagFilter(
                await ApplyRepositoryFilter(_db.RepositoryConnections.AsNoTracking(), filter).ToListAsync(ct),
                filter, c => c.Tags).Count;
        return await ApplyRepositoryFilter(_db.RepositoryConnections.AsNoTracking(), filter).CountAsync(ct);
    }

    public Task<RepositoryConnection?> GetRepositoryConnectionAsync(Guid id, CancellationToken ct = default) =>
        _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<RepositoryConnection> SaveRepositoryConnectionAsync(
        SaveRepositoryConnectionRequest request, CancellationToken ct = default)
    {
        var entity = request.Id is { } id
            ? await _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct)
              ?? throw new InvalidOperationException($"Repository connection {id} not found.")
            : new RepositoryConnection();

        entity.ProviderType = request.ProviderType;
        entity.DisplayName = request.DisplayName.Trim();
        entity.Description = Trimmed(request.Description);
        entity.ClientId = request.ClientId;
        entity.BaseUrl = Trimmed(request.BaseUrl);
        entity.Organisation = Trimmed(request.Organisation);
        entity.ProjectOrWorkspace = Trimmed(request.ProjectOrWorkspace);
        entity.Tags = request.Tags?.ToList() ?? entity.Tags;
        entity.IsFavorite = request.IsFavorite;

        await ApplyRepositorySecretAsync(entity, request, ct);

        if (request.Id is null)
            _db.RepositoryConnections.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    private async Task ApplyRepositorySecretAsync(
        RepositoryConnection entity, SaveRepositoryConnectionRequest request, CancellationToken ct)
    {
        if (request.ClearToken)
        {
            await RemoveSecretAsync(entity.SecretReferenceId, ct);
            entity.SecretReferenceId = null;
            entity.LastStatus = ConnectionStatus.Untested;
            entity.LastTestedAt = null;
            return;
        }

        if (string.IsNullOrWhiteSpace(request.AccessToken))
            return; // leave the existing secret untouched

        var reference = entity.SecretReferenceId is { } refId
            ? await _db.SecretReferences.FirstOrDefaultAsync(r => r.Id == refId, ct)
            : null;

        if (reference is null)
        {
            reference = new SecretReference
            {
                Provider = SecretProvider.WindowsCredentialManager,
                ReferenceKey = $"Fenrix:repo:{entity.Id:N}",
                DisplayName = $"{entity.DisplayName} ({entity.ProviderType})"
            };
            _db.SecretReferences.Add(reference);
            entity.SecretReferenceId = reference.Id;
        }
        else
        {
            reference.DisplayName = $"{entity.DisplayName} ({entity.ProviderType})";
        }

        var blob = new RepositoryCredential(request.AccessToken.Trim(), Trimmed(request.UserName)).Serialize();
        await _secrets.StoreAsync(reference, blob, ct);
        entity.LastStatus = ConnectionStatus.Untested;
        entity.LastTestedAt = null;
    }

    public async Task<ConnectionTestResult> TestRepositoryConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return ConnectionTestResult.Fail("Connection not found.");

        var resolved = await _providerFactory.ResolveAsync(id, ct);
        ConnectionTestResult result;

        if (resolved is null)
        {
            result = ConnectionTestResult.Fail("Could not resolve a provider for this connection.");
        }
        else if (entity.ProviderType == RepositoryProviderType.GenericGit)
        {
            // Generic Git has no host API to probe; a stored credential is used by git directly.
            result = resolved.Value.Context.HasToken
                ? ConnectionTestResult.Ok("Generic Git — credentials will be used by git directly (no host API to verify).")
                : ConnectionTestResult.Ok("Generic Git — no stored credential; git will use the OS credential helper.");
        }
        else if (!resolved.Value.Context.HasToken)
        {
            result = ConnectionTestResult.Fail("No access token is stored for this connection. Add one, then test again.");
        }
        else
        {
            var user = await resolved.Value.Provider.GetCurrentUserAsync(resolved.Value.Context, ct);
            result = user.Succeeded
                ? ConnectionTestResult.Ok(user.Value!.DisplayName ?? user.Value!.UserName)
                : ConnectionTestResult.Fail(user.Guidance ?? user.ErrorMessage ?? "Test failed.");
        }

        entity.LastStatus = result.Succeeded ? ConnectionStatus.Ok : ConnectionStatus.Failed;
        entity.LastTestedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    // ---- cloud connections ----

    public async Task<IReadOnlyList<CloudConnection>> GetCloudConnectionsAsync(
        ConnectionFilter filter, CancellationToken ct = default)
    {
        var query = ApplyCloudFilter(_db.CloudConnections.AsNoTracking(), filter);
        var rows = await query
            .OrderByDescending(c => c.IsFavorite)
            .ThenBy(c => c.DisplayName)
            .ToListAsync(ct);
        rows = ApplyTagFilter(rows, filter, c => c.Tags);
        return Page(rows, filter);
    }

    public async Task<int> CountCloudConnectionsAsync(ConnectionFilter filter, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(filter.Tag))
            return ApplyTagFilter(
                await ApplyCloudFilter(_db.CloudConnections.AsNoTracking(), filter).ToListAsync(ct),
                filter, c => c.Tags).Count;
        return await ApplyCloudFilter(_db.CloudConnections.AsNoTracking(), filter).CountAsync(ct);
    }

    public Task<CloudConnection?> GetCloudConnectionAsync(Guid id, CancellationToken ct = default) =>
        _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<CloudConnection> SaveCloudConnectionAsync(
        SaveCloudConnectionRequest request, CancellationToken ct = default)
    {
        var entity = request.Id is { } id
            ? await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct)
              ?? throw new InvalidOperationException($"Cloud connection {id} not found.")
            : new CloudConnection();

        entity.ProviderType = request.ProviderType;
        entity.DisplayName = request.DisplayName.Trim();
        entity.Description = Trimmed(request.Description);
        entity.ClientId = request.ClientId;
        entity.TenantOrAccountId = Trimmed(request.TenantOrAccountId);
        entity.SubscriptionOrProjectId = Trimmed(request.SubscriptionOrProjectId);
        entity.Region = Trimmed(request.Region);
        entity.ProfileName = Trimmed(request.ProfileName);
        entity.Client = Trimmed(request.ServicePrincipalClientId);
        if (request.MetadataJson is not null)
            entity.MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson.Trim();
        entity.Tags = request.Tags?.ToList() ?? entity.Tags;
        entity.IsFavorite = request.IsFavorite;

        if (request.ClearSecret)
        {
            await RemoveSecretAsync(entity.SecretReferenceId, ct);
            entity.SecretReferenceId = null;
            entity.LastStatus = ConnectionStatus.Untested;
            entity.LastTestedAt = null;
        }
        else if (!string.IsNullOrWhiteSpace(request.SecretValue))
        {
            var reference = entity.SecretReferenceId is { } refId
                ? await _db.SecretReferences.FirstOrDefaultAsync(r => r.Id == refId, ct)
                : null;
            if (reference is null)
            {
                reference = new SecretReference
                {
                    Provider = SecretProvider.WindowsCredentialManager,
                    ReferenceKey = $"Fenrix:cloud:{entity.Id:N}",
                    DisplayName = $"{entity.DisplayName} ({entity.ProviderType})"
                };
                _db.SecretReferences.Add(reference);
                entity.SecretReferenceId = reference.Id;
            }
            await _secrets.StoreAsync(reference, request.SecretValue.Trim(), ct);
            entity.LastStatus = ConnectionStatus.Untested;
            entity.LastTestedAt = null;
        }

        if (request.Id is null)
            _db.CloudConnections.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<ConnectionTestResult> TestCloudConnectionAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (entity is null)
            return ConnectionTestResult.Fail("Connection not found.");

        var resolved = await _cloudFactory.ResolveAsync(id, ct);
        ConnectionTestResult result;
        if (resolved is null)
        {
            result = ConnectionTestResult.Fail("No cloud adapter is available for this connection's provider.");
        }
        else
        {
            var test = await resolved.Value.Provider.TestAsync(resolved.Value.Context, ct);
            result = test.Succeeded
                ? ConnectionTestResult.Ok(test.Value!.DisplayName is { } d ? $"{test.Value.Account} ({d})" : test.Value.Account)
                : ConnectionTestResult.Fail(test.Guidance ?? test.ErrorMessage ?? "Test failed.");
        }

        entity.LastStatus = result.Succeeded ? ConnectionStatus.Ok : ConnectionStatus.Failed;
        entity.LastTestedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<IReadOnlyList<Contracts.Cloud.CloudScope>> GetCloudScopesAsync(Guid id, CancellationToken ct = default)
    {
        var resolved = await _cloudFactory.ResolveAsync(id, ct);
        if (resolved is null)
            return [];
        var scopes = await resolved.Value.Provider.GetAvailableScopesAsync(resolved.Value.Context, ct);
        return scopes.Succeeded ? scopes.Value! : [];
    }

    // ---- shared lifecycle ----

    public async Task SetFavoriteAsync(Guid id, bool favorite, CancellationToken ct = default)
    {
        var repo = await _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (repo is not null) { repo.IsFavorite = favorite; await _db.SaveChangesAsync(ct); return; }
        var cloud = await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cloud is not null) { cloud.IsFavorite = favorite; await _db.SaveChangesAsync(ct); }
    }

    public async Task<bool> ArchiveAsync(Guid id, bool archived, CancellationToken ct = default)
    {
        // Only block archival while still referenced; un-archiving is always allowed.
        if (archived && await GetUsageCountAsync(id, ct) > 0)
            return false;

        var repo = await _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (repo is not null) { repo.IsArchived = archived; await _db.SaveChangesAsync(ct); return true; }
        var cloud = await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cloud is not null) { cloud.IsArchived = archived; await _db.SaveChangesAsync(ct); return true; }
        return false;
    }

    public async Task<int> GetUsageCountAsync(Guid id, CancellationToken ct = default)
    {
        var repoUse = await _db.Projects.CountAsync(p => p.RepositoryConnectionId == id, ct);
        var cloudUse = await _db.Environments.CountAsync(e => e.CloudConnectionId == id, ct);
        return repoUse + cloudUse;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await GetUsageCountAsync(id, ct) > 0)
            return false;

        var repo = await _db.RepositoryConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (repo is not null)
        {
            await RemoveSecretAsync(repo.SecretReferenceId, ct);
            _db.RepositoryConnections.Remove(repo);
            await _db.SaveChangesAsync(ct);
            return true;
        }

        var cloud = await _db.CloudConnections.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cloud is not null)
        {
            await RemoveSecretAsync(cloud.SecretReferenceId, ct);
            _db.CloudConnections.Remove(cloud);
            await _db.SaveChangesAsync(ct);
            return true;
        }
        return false;
    }

    // ---- clients ----

    public async Task<IReadOnlyList<Client>> GetClientsAsync(CancellationToken ct = default) =>
        await _db.Clients.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public async Task<Client> SaveClientAsync(Client client, CancellationToken ct = default)
    {
        var existing = await _db.Clients.FirstOrDefaultAsync(c => c.Id == client.Id, ct);
        if (existing is null)
            _db.Clients.Add(client);
        else
        {
            existing.Name = client.Name;
            existing.Code = client.Code;
            existing.Description = client.Description;
            existing.Tags = client.Tags;
        }
        await _db.SaveChangesAsync(ct);
        return existing ?? client;
    }

    // ---- helpers ----

    private async Task RemoveSecretAsync(Guid? secretReferenceId, CancellationToken ct)
    {
        if (secretReferenceId is not { } refId)
            return;
        var reference = await _db.SecretReferences.FirstOrDefaultAsync(r => r.Id == refId, ct);
        if (reference is null)
            return;
        try { await _secrets.DeleteAsync(reference, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not remove secret for reference {Ref}.", refId); }
        _db.SecretReferences.Remove(reference);
    }

    private static IQueryable<RepositoryConnection> ApplyRepositoryFilter(
        IQueryable<RepositoryConnection> query, ConnectionFilter filter)
    {
        if (!filter.IncludeArchived)
            query = query.Where(c => !c.IsArchived);
        if (filter.ClientId is { } clientId)
            query = query.Where(c => c.ClientId == clientId);
        if (filter.Favorite is { } fav)
            query = query.Where(c => c.IsFavorite == fav);
        if (filter.Status is { } status)
            query = query.Where(c => c.LastStatus == status);
        if (!string.IsNullOrWhiteSpace(filter.ProviderType)
            && Enum.TryParse<RepositoryProviderType>(filter.ProviderType, out var pt))
            query = query.Where(c => c.ProviderType == pt);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(c =>
                c.DisplayName.Contains(s) ||
                (c.Description != null && c.Description.Contains(s)) ||
                (c.Organisation != null && c.Organisation.Contains(s)) ||
                (c.BaseUrl != null && c.BaseUrl.Contains(s)));
        }
        return query;
    }

    private static IQueryable<CloudConnection> ApplyCloudFilter(
        IQueryable<CloudConnection> query, ConnectionFilter filter)
    {
        if (!filter.IncludeArchived)
            query = query.Where(c => !c.IsArchived);
        if (filter.ClientId is { } clientId)
            query = query.Where(c => c.ClientId == clientId);
        if (filter.Favorite is { } fav)
            query = query.Where(c => c.IsFavorite == fav);
        if (filter.Status is { } status)
            query = query.Where(c => c.LastStatus == status);
        if (!string.IsNullOrWhiteSpace(filter.ProviderType)
            && Enum.TryParse<CloudProviderType>(filter.ProviderType, out var pt))
            query = query.Where(c => c.ProviderType == pt);
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var s = filter.Search.Trim();
            query = query.Where(c =>
                c.DisplayName.Contains(s) ||
                (c.Description != null && c.Description.Contains(s)) ||
                (c.TenantOrAccountId != null && c.TenantOrAccountId.Contains(s)) ||
                (c.Region != null && c.Region.Contains(s)));
        }
        return query;
    }

    // Tags are a JSON column (not SQL-queryable), so this narrow filter is client-evaluated after the
    // SQL-side filters have already reduced the set. See docs/26-connections.md (tags/facets).
    private static List<T> ApplyTagFilter<T>(List<T> rows, ConnectionFilter filter, Func<T, List<string>> tags)
    {
        if (string.IsNullOrWhiteSpace(filter.Tag))
            return rows;
        return rows.Where(r => tags(r).Contains(filter.Tag, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    private static IReadOnlyList<T> Page<T>(List<T> rows, ConnectionFilter filter)
    {
        var skip = Math.Max(0, filter.Skip);
        var take = Math.Clamp(filter.Take, 1, 1000);
        return rows.Skip(skip).Take(take).ToList();
    }

    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
