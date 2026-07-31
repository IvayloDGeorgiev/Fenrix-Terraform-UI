using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Contracts.Deployments;

/// <summary>The kinds of non-interactive gate evaluated before a governed deploy. See docs/20-pipelines-deployments.md.</summary>
public enum DeployGateKind
{
    CloudConnection = 0,
    RepositoryAtVersion = 1,
    RequiredBranch = 2,
    CleanWorkingTree = 3,
    PreviousStageSuccess = 4
}

/// <summary>
/// One evaluated stage gate. Blockers must pass before deploy; non-blockers are advisory warnings. The
/// interactive gates (approval acknowledgement, production typed-confirmation) are surfaced separately on
/// <see cref="DeployPreparation"/> because they are satisfied by the user at execute time.
/// </summary>
public sealed record DeployGate(DeployGateKind Kind, string Label, bool Passed, bool IsBlocker, string? Detail);

/// <summary>
/// Everything the UI needs to review, gate, and then execute one governed deploy of a version to one
/// environment: the resolved version + target, whether the repository is at the version's commit (and can be
/// checked out), the saved plan + redacted review produced by the governed plan, the evaluated gates, and the
/// interactive requirements. Mirrors the plan → gates → apply flow (ADR-0003). See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record DeployPreparation(
    Guid ProjectId,
    Guid ProjectVersionId,
    string VersionLabel,
    Guid EnvironmentId,
    string EnvironmentName,
    bool IsProduction,
    string VersionCommit,
    bool RepositoryAtVersion,
    bool CanCheckout,
    Guid? SavedPlanId,
    PlanReview? Review,
    CommandPreview? PlanPreview,
    IReadOnlyList<DeployGate> Gates,
    bool RequiresApproval,
    bool RequiresTypedConfirmation,
    string? ConfirmationPhrase,
    string? BlockReason,
    // Phase 11 — role-gated approval state. When UsesRoleGatedApproval is true (enterprise mode on), the UI
    // shows request/awaiting/approved and gates on ApprovalGranted; when false, the prior local self-ack applies.
    // Defaults keep single-user callers unchanged.
    bool ApprovalGranted = false,
    bool ApprovalRequested = false,
    bool UsesRoleGatedApproval = false)
{
    /// <summary>True when a plan exists and every blocking gate passes (interactive gates enforced at execute).</summary>
    public bool PlanReady =>
        BlockReason is null && SavedPlanId is not null && Gates.Where(g => g.IsBlocker).All(g => g.Passed);

    public static DeployPreparation Blocked(Guid projectId, Guid versionId, string label, Guid envId, string envName, bool prod, string commit, string reason) =>
        new(projectId, versionId, label, envId, envName, prod, commit, false, false, null, null, null, [], false, false, null, reason);
}

/// <summary>The user's confirmation for a governed deploy: approval acknowledgement and any typed production value.</summary>
public sealed record DeployConfirmation(bool Approved = false, string? TypedValue = null);

/// <summary>Outcome of executing one governed deploy. See docs/20-pipelines-deployments.md.</summary>
public sealed record DeployExecutionResult(
    bool Succeeded,
    Guid? DeploymentId,
    ApplyResult? Apply,
    string? Error)
{
    public static DeployExecutionResult Fail(string error) => new(false, null, null, error);
}

/// <summary>The result of one environment within a governed fan-out (deploy one version to many/all).</summary>
public enum FanOutOutcome
{
    /// <summary>Planned, gated, and applied automatically (no interactive gate pending).</summary>
    Deployed = 0,

    /// <summary>Prepared but needs the user to approve and/or type the production confirmation.</summary>
    NeedsConfirmation = 1,

    /// <summary>A blocking gate failed (e.g. unbound cloud, wrong branch) before apply.</summary>
    Blocked = 2,

    /// <summary>The plan or apply failed.</summary>
    Failed = 3
}

/// <summary>One environment's result inside a fan-out. Fan-out is not a transaction — each is independent.</summary>
public sealed record FanOutItemResult(
    Guid EnvironmentId,
    string EnvironmentName,
    FanOutOutcome Outcome,
    Guid? DeploymentId,
    Guid? SavedPlanId,
    string? Detail);

/// <summary>The aggregate result of deploying one version to several/all environments.</summary>
public sealed record FanOutResult(Guid ProjectVersionId, IReadOnlyList<FanOutItemResult> Items);
