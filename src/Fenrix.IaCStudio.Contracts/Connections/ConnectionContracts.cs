using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Contracts.Connections;

/// <summary>
/// Create/update payload for a repository (Git host) connection. Carries identifying metadata plus an
/// <em>optional, transient</em> access token: when present it is written to the OS secret store and only a
/// <c>SecretReference</c> is persisted — the token never touches the database. Leave the token null to keep
/// the existing secret unchanged. See docs/11-secrets.md, docs/26-connections.md.
/// </summary>
public sealed record SaveRepositoryConnectionRequest(
    Guid? Id,
    RepositoryProviderType ProviderType,
    string DisplayName,
    string? Description,
    Guid? ClientId,
    string? BaseUrl,
    string? Organisation,
    string? ProjectOrWorkspace,
    IReadOnlyList<string>? Tags,
    bool IsFavorite,
    string? AccessToken,
    string? UserName,
    bool ClearToken = false);

/// <summary>
/// Create/update payload for a cloud connection: identifying metadata plus an <em>optional, transient</em>
/// secret (an Azure service-principal client secret) that, when present, is written to the OS secret store so
/// only a <c>SecretReference</c> is persisted. <paramref name="ServicePrincipalClientId"/> is a non-secret
/// identifier (maps to <c>CloudConnection.Client</c>); <paramref name="MetadataJson"/> carries
/// provider-specific extras (e.g. Azure auth mode, a Google service-account file path). Leave the secret null
/// to keep the existing one unchanged. See docs/10-cloud-integrations.md, docs/11-secrets.md.
/// </summary>
public sealed record SaveCloudConnectionRequest(
    Guid? Id,
    CloudProviderType ProviderType,
    string DisplayName,
    string? Description,
    Guid? ClientId,
    string? TenantOrAccountId,
    string? SubscriptionOrProjectId,
    string? Region,
    string? ProfileName,
    IReadOnlyList<string>? Tags,
    bool IsFavorite,
    string? SecretValue = null,
    bool ClearSecret = false,
    string? ServicePrincipalClientId = null,
    string? MetadataJson = null);

/// <summary>Which slice of the connection library to return — filters + paging for the virtualized hub.</summary>
public sealed record ConnectionFilter(
    string? Search = null,
    Guid? ClientId = null,
    string? ProviderType = null,
    string? Tag = null,
    ConnectionStatus? Status = null,
    bool? Favorite = null,
    bool IncludeArchived = false,
    int Skip = 0,
    int Take = 100);

/// <summary>The outcome of testing a connection (records LastStatus/LastTestedAt on the row).</summary>
public sealed record ConnectionTestResult(
    bool Succeeded,
    string? Identity,
    string? Message)
{
    public static ConnectionTestResult Ok(string? identity) => new(true, identity, null);
    public static ConnectionTestResult Fail(string message) => new(false, null, message);
}
