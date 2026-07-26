using Fenrix.IaCStudio.Contracts.Connections;
using Fenrix.IaCStudio.Domain.Cloud;

namespace Fenrix.IaCStudio.Application.Abstractions.Connections;

/// <summary>
/// The global Connections library: CRUD over cloud and repository connections and their owning clients,
/// searchable/pageable at scale (hundreds–thousands of rows), with secret handling delegated to the OS store
/// so only a <c>SecretReference</c> is persisted. Repository connections are bound per project; cloud
/// connections are bound per environment. See docs/26-connections.md, docs/11-secrets.md.
/// </summary>
public interface IConnectionService
{
    // ---- repository (Git host) connections ----

    Task<IReadOnlyList<RepositoryConnection>> GetRepositoryConnectionsAsync(
        ConnectionFilter filter, CancellationToken ct = default);

    Task<int> CountRepositoryConnectionsAsync(ConnectionFilter filter, CancellationToken ct = default);

    Task<RepositoryConnection?> GetRepositoryConnectionAsync(Guid id, CancellationToken ct = default);

    /// <summary>Creates or updates a repository connection; writes/updates its token in the secret store.</summary>
    Task<RepositoryConnection> SaveRepositoryConnectionAsync(
        SaveRepositoryConnectionRequest request, CancellationToken ct = default);

    /// <summary>Tests a repository connection against its provider adapter and records the result.</summary>
    Task<ConnectionTestResult> TestRepositoryConnectionAsync(Guid id, CancellationToken ct = default);

    // ---- cloud connections ----

    Task<IReadOnlyList<CloudConnection>> GetCloudConnectionsAsync(
        ConnectionFilter filter, CancellationToken ct = default);

    Task<int> CountCloudConnectionsAsync(ConnectionFilter filter, CancellationToken ct = default);

    Task<CloudConnection?> GetCloudConnectionAsync(Guid id, CancellationToken ct = default);

    Task<CloudConnection> SaveCloudConnectionAsync(
        SaveCloudConnectionRequest request, CancellationToken ct = default);

    // ---- shared lifecycle ----

    /// <summary>Sets favorite state on a connection of either kind.</summary>
    Task SetFavoriteAsync(Guid id, bool favorite, CancellationToken ct = default);

    /// <summary>Archives (soft-hides) a connection; blocked when it is still referenced by a project/environment.</summary>
    Task<bool> ArchiveAsync(Guid id, bool archived, CancellationToken ct = default);

    /// <summary>How many projects/environments reference a connection (guards deletion/archival).</summary>
    Task<int> GetUsageCountAsync(Guid id, CancellationToken ct = default);

    /// <summary>Deletes a connection and its secret; refuses while in use (returns false).</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // ---- clients (owning groups) ----

    Task<IReadOnlyList<Client>> GetClientsAsync(CancellationToken ct = default);

    Task<Client> SaveClientAsync(Client client, CancellationToken ct = default);
}
