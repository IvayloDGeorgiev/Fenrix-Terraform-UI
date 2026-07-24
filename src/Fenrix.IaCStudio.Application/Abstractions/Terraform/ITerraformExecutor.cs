using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Orchestrates a typed Terraform command end to end: resolve context (binary, working directory,
/// version constraint), build the exact request + preview, run it through the process runner while
/// streaming output, and record redacted history. <see cref="PlanAsync"/> is side-effect-free so the UI
/// can show a live preview; <see cref="ExecuteAsync"/> runs a previously built plan. See
/// docs/05-terraform-engine.md, docs/23-command-transparency.md, docs/25-execution-lifecycle.md.
/// </summary>
public interface ITerraformExecutor
{
    /// <summary>
    /// Resolves everything needed to run the given spec and returns a plan containing the exact request,
    /// its redacted preview, the resolved binary, and any block reason (e.g. version-constraint
    /// violation, missing binary, or missing working directory). Performs no side effects.
    /// </summary>
    Task<TerraformRunPlan> PlanAsync(TerraformRunSpec spec, CancellationToken ct = default);

    /// <summary>
    /// Executes a runnable plan, streaming each output line via <paramref name="output"/> and recording
    /// a redacted history entry. Throws <see cref="InvalidOperationException"/> if the plan is blocked.
    /// </summary>
    Task<TerraformRunResult> ExecuteAsync(
        TerraformRunPlan plan,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default);
}
