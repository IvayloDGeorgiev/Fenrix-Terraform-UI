using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Domain.Cloud;

/// <summary>
/// A version-control host connection bound at the project level (a project maps to
/// one repository). Holds a secret reference, never a secret. See docs/26-connections.md.
/// </summary>
public sealed class RepositoryConnection
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public RepositoryProviderType ProviderType { get; set; } = RepositoryProviderType.GenericGit;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }

    public Guid? ClientId { get; set; }

    public string? BaseUrl { get; set; }
    public string? Organisation { get; set; }
    public string? ProjectOrWorkspace { get; set; }

    public Guid? SecretReferenceId { get; set; }

    public List<string> Tags { get; set; } = [];
    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }

    public ConnectionStatus LastStatus { get; set; } = ConnectionStatus.Untested;
    public DateTimeOffset? LastTestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
