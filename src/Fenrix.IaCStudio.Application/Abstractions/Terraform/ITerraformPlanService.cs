using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Runs the saved-plan half of the workflow: resolve context and preview the exact <c>plan</c> command,
/// execute it (streaming), convert it with <c>show -json</c>, hash it for integrity, and persist the
/// redacted safety record. The previewed command in <see cref="PlanContext"/> is exactly what runs. See
/// docs/06-plan-apply-safety.md, docs/23-command-transparency.md, docs/25-execution-lifecycle.md.
/// </summary>
public interface ITerraformPlanService
{
    /// <summary>
    /// Resolves the working directory, var-file, and a fresh <c>-out</c> target for a plan, builds the
    /// redacted preview, and computes any block reason (missing binary, version constraint, environment
    /// locked, …). Side-effect-free: no plan file is written.
    /// </summary>
    Task<PlanContext> PreparePlanAsync(
        Guid projectId, Guid environmentId, PlanOptions options, CancellationToken ct = default);

    /// <summary>
    /// Runs the plan described by <paramref name="context"/> (streaming output), then <c>show -json</c>,
    /// persists the saved plan with its integrity hashes and redacted counts, and returns the parsed,
    /// redacted review. Acquires the per-environment lock for the duration.
    /// </summary>
    Task<PlanCreationResult> CreatePlanAsync(
        PlanContext context, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>Re-derives the redacted review for a saved plan by re-running <c>show -json</c> on its file.</summary>
    Task<PlanReview> GetReviewAsync(Guid savedPlanId, CancellationToken ct = default);

    /// <summary>Recent saved plans for a project (optionally one environment), newest first.</summary>
    Task<IReadOnlyList<SavedPlanSummary>> GetRecentAsync(
        Guid projectId, Guid? environmentId = null, int limit = 25, CancellationToken ct = default);
}
