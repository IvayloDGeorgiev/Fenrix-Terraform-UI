namespace Fenrix.IaCStudio.Domain.Versioning;

/// <summary>
/// A per-project ordered release pipeline: the sequence of environment stages (Dev → UAT → Live) plus the
/// per-stage governance rules. Pipelines are optional — a project without one still deploys per-environment
/// from the Terraform / Pipelines pages. Nothing here weakens the saved-plan-only apply rule (ADR-0003); the
/// rules simply add gates in front of the standard governed plan/apply. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class DeploymentPipeline
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }

    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The ordered stages of this pipeline (first = earliest environment, e.g. Dev).</summary>
    public List<PipelineStage> Stages { get; set; } = [];
}
