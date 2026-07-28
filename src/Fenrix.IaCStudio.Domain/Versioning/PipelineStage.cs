namespace Fenrix.IaCStudio.Domain.Versioning;

/// <summary>
/// One stage of a <see cref="DeploymentPipeline"/>: the environment it targets plus the gates that must pass
/// before its governed apply runs. Defaults mirror the Dev → UAT → Live convention with production gates on
/// the last stage. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class PipelineStage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid PipelineId { get; init; }
    public Guid EnvironmentId { get; set; }

    /// <summary>0-based position in the pipeline (drives the promotion path and "must promote in order").</summary>
    public int Order { get; set; }

    /// <summary>Require an explicit local approval acknowledgement before apply (self-ack this phase).</summary>
    public bool RequireApproval { get; set; }

    /// <summary>The upstream stage must have a successful deployment of the same version first.</summary>
    public bool RequirePreviousStageSuccess { get; set; }

    /// <summary>Refuse to deploy when the working tree has uncommitted changes.</summary>
    public bool RequireCleanWorkingTree { get; set; }

    /// <summary>Production stages require typing the environment name to confirm (ADR-0003).</summary>
    public bool RequireTypedConfirmationForProduction { get; set; } = true;

    /// <summary>When set, only this branch may be deployed to the stage (e.g. only 'main' to Live).</summary>
    public string? RequiredBranch { get; set; }

    /// <summary>Enterprise: role-gated approver identities. Recorded now; enforced with the agent (Phase 11).</summary>
    public List<string> Approvers { get; set; } = [];
}
