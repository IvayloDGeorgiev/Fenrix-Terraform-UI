namespace Fenrix.IaCStudio.Domain.Versioning;

/// <summary>
/// A per-project version anchored to an immutable Git snapshot. Versions are
/// environment-independent and can be deployed to any/all environments.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class ProjectVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }

    public string Label { get; init; } = string.Empty;     // e.g. "1.0", "1.5-rc", "2.0-dev"
    public string GitCommit { get; init; } = string.Empty; // immutable config snapshot
    public string? GitTag { get; init; }
    public string? GitBranch { get; init; }
    public string ConfigurationHash { get; init; } = string.Empty;
    public string ProviderLockHash { get; init; } = string.Empty;
    public string? RequiredTerraformVersion { get; init; }

    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = string.Empty;
}
