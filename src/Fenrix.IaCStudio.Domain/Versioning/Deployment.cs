using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Domain.Versioning;

/// <summary>
/// A record of a governed deployment of a <see cref="ProjectVersion"/> to one
/// environment. An environment's current version is its latest Succeeded deployment.
/// Stores summaries/hashes/references only — never secrets. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class Deployment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ProjectVersionId { get; init; }
    public Guid? PlanId { get; init; }

    public string VersionLabel { get; init; } = string.Empty;
    public string GitCommit { get; init; } = string.Empty;
    public string GitBranch { get; init; } = string.Empty;
    public string ConfigurationHash { get; init; } = string.Empty;
    public string ProviderLockHash { get; init; } = string.Empty;
    public string TerraformVersion { get; init; } = string.Empty;

    public string? StateBackend { get; init; }
    public long? StateSerial { get; init; }
    public string? StateLineage { get; init; }

    public DeploymentStatus Status { get; set; } = DeploymentStatus.Queued;
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public string InitiatedBy { get; init; } = string.Empty;

    public int AddCount { get; set; }
    public int ChangeCount { get; set; }
    public int DestroyCount { get; set; }
    public int ReplaceCount { get; set; }
}
