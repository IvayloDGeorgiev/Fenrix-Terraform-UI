using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// Everything needed to preview and then run one plan: the resolved working directory, var-file, and the
/// exact <c>-out</c> target (an as-yet-unwritten file under the project), plus the runnable spec, its
/// redacted preview, and any block reason. The UI shows <see cref="Preview"/> and, if <see cref="CanRun"/>,
/// passes this same context to <c>CreatePlanAsync</c> — so the previewed command is exactly what runs.
/// See docs/06-plan-apply-safety.md and docs/23-command-transparency.md.
/// </summary>
public sealed record PlanContext(
    Guid ProjectId,
    Guid EnvironmentId,
    Guid PlanId,
    PlanMode Mode,
    string WorkingDirectory,
    string? VarFile,
    string OutPlanFile,
    string RelativePlanFile,
    bool IsProductionTarget,
    Guid? CloudConnectionId,
    TerraformRunSpec Spec,
    CommandPreview Preview,
    string? BlockReason)
{
    /// <summary>True when the plan may be executed (binary found, version satisfied, not locked).</summary>
    public bool CanRun => BlockReason is null;
}

/// <summary>
/// The result of creating a plan: the persisted saved plan and its redacted review when the plan
/// succeeded, or a block reason / non-zero process result when it did not. See docs/06-plan-apply-safety.md.
/// </summary>
public sealed record PlanCreationResult(
    Guid? SavedPlanId,
    SavedPlanSummary? SavedPlan,
    PlanReview? Review,
    ProcessResult? Process,
    Guid? RunId,
    string? OutputLogPath,
    string? BlockReason)
{
    /// <summary>True when a saved plan was produced and is ready to review/apply.</summary>
    public bool Succeeded => SavedPlanId is not null && BlockReason is null;

    /// <summary>A plan blocked before it ran (lock held, version constraint, missing directory, …).</summary>
    public static PlanCreationResult Blocked(string reason) =>
        new(null, null, null, null, null, null, reason);
}
