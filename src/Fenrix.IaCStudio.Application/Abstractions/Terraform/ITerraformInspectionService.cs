using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// The read-only side of the Terraform loop: browse current state, read outputs, and render the dependency
/// graph — all without executing providers against real systems. Every query reuses the <c>-json</c>/no-log
/// posture (sensitive output is parsed in memory and redacted, never written to a log), composes the bound
/// cloud connection's environment when one exists, and never takes the per-environment lock. Refresh-only
/// drift (Phase 4) is surfaced here by delegating to the plan service. See docs/25-execution-lifecycle.md
/// "Read-only inspection", docs/05-terraform-engine.md, docs/22-terraform-files-model.md.
/// </summary>
public interface ITerraformInspectionService
{
    /// <summary>Builds the redacted preview for a read-only inspection command (for the command panel).</summary>
    Task<InspectionContext> PreviewAsync(
        Guid projectId, Guid environmentId, TerraformCommandKind kind, CancellationToken ct = default);

    /// <summary>Runs <c>show -json</c> on the current state and returns the parsed, redacted snapshot.</summary>
    Task<StateSnapshot> GetStateAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>Runs <c>output -json</c> and returns the parsed outputs (sensitive values redacted).</summary>
    Task<OutputCollection> GetOutputsAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>Runs <c>graph</c> and returns the parsed dependency graph for the visual renderer.</summary>
    Task<DependencyGraph> GetGraphAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Runs a refresh-only plan (Phase 4) to detect drift and returns the parsed review. Delegates to the
    /// plan service, so it takes the environment lock and records a saved plan like any other plan.
    /// </summary>
    Task<PlanCreationResult> CheckDriftAsync(
        Guid projectId, Guid environmentId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
}
