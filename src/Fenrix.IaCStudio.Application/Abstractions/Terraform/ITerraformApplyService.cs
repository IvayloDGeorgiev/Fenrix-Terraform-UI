using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Runs the apply half of the workflow against an <em>exact saved plan</em> (ADR-0003): evaluate the
/// safety gates, require typed confirmation for production, acquire the per-environment lock, execute
/// <c>apply -json</c> while streaming structured per-resource progress, and record the outcome. See
/// docs/06-plan-apply-safety.md and docs/25-execution-lifecycle.md.
/// </summary>
public interface ITerraformApplyService
{
    /// <summary>
    /// Evaluates the apply safety gates for a saved plan: plan-file existence + hash, configuration/lock
    /// integrity (invalidation), environment + cloud-account unchanged, and warnings for
    /// deletions/replacements/production. Returns the exact apply preview and whether apply may proceed.
    /// </summary>
    Task<ApplyPreflight> PreflightAsync(Guid savedPlanId, CancellationToken ct = default);

    /// <summary>
    /// Applies the exact saved plan. Re-verifies preflight, enforces the typed production confirmation,
    /// acquires the environment lock, runs <c>apply -json</c> (raw stream via <paramref name="rawOutput"/>,
    /// structured per-resource events via <paramref name="progress"/>), and marks the plan applied.
    /// Throws <see cref="InvalidOperationException"/> if a gate blocks the apply.
    /// </summary>
    Task<ApplyResult> ApplyAsync(
        Guid savedPlanId,
        ApplyConfirmation confirmation,
        IProgress<ProcessOutputEvent>? rawOutput,
        IProgress<ApplyProgressEvent>? progress,
        CancellationToken ct = default);
}
