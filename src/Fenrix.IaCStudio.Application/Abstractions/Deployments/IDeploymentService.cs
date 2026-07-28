using Fenrix.IaCStudio.Contracts.Deployments;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Deployments;

/// <summary>
/// The deployments engine: read-only board + version-matrix views over existing plan/apply + Git history, and
/// the governed deploy flow (plan → gates → apply the exact saved plan, ADR-0003) with promote, rollback, and
/// fan-out. Nothing here bypasses the safety gates — "one-click deploy" means one click to start a governed,
/// saved-plan apply. See docs/20-pipelines-deployments.md.
/// </summary>
public interface IDeploymentService
{
    // ---- read models ----

    /// <summary>Builds the release-pipeline board: ordered environment stages + recent deployment history.</summary>
    Task<DeploymentBoard> GetBoardAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Builds the versions (rows) × environments (columns) deploy-state matrix.</summary>
    Task<VersionMatrix> GetMatrixAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Recent deployments for a project (optionally one environment), newest first.</summary>
    Task<IReadOnlyList<DeploymentSummary>> GetDeploymentsAsync(
        Guid projectId, Guid? environmentId = null, int limit = 50, CancellationToken ct = default);

    // ---- governed deploy ----

    /// <summary>
    /// Prepares a governed deploy of a version to one environment: ensures the repository is at the version's
    /// commit (reporting whether a checkout is needed / possible), runs the governed plan, evaluates the stage
    /// gates, and returns the review + interactive requirements. Side-effect-free beyond creating the saved plan.
    /// </summary>
    Task<DeployPreparation> PrepareDeployAsync(
        Guid versionId, Guid environmentId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>
    /// Checks out the version's commit/tag into the working tree (only when clean), so a blocked
    /// "repository at version" gate can be satisfied before re-preparing. Returns the git result message.
    /// </summary>
    Task<(bool Ok, string? Detail)> CheckoutVersionAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Executes a prepared deploy: re-evaluates the gates, enforces the approval acknowledgement + typed
    /// production confirmation, applies the exact saved plan, and records the <c>Deployment</c> (version,
    /// state serial/lineage, counts). See docs/20-pipelines-deployments.md.
    /// </summary>
    Task<DeployExecutionResult> ExecuteDeployAsync(
        Guid savedPlanId,
        DeployConfirmation confirmation,
        IProgress<ProcessOutputEvent>? rawOutput,
        IProgress<ApplyProgressEvent>? progress,
        CancellationToken ct = default);

    // ---- promote / rollback resolution ----

    /// <summary>The current (latest Succeeded) version of an environment, or null. Used as a promote source.</summary>
    Task<Guid?> GetCurrentVersionIdAsync(Guid environmentId, CancellationToken ct = default);

    /// <summary>The version to roll back to (the previous distinct Succeeded deployment's version), or null.</summary>
    Task<Guid?> GetRollbackVersionIdAsync(Guid environmentId, CancellationToken ct = default);

    // ---- fan-out ----

    /// <summary>
    /// Deploys one version to several/all environments as independent governed deployments: each is prepared +
    /// gated, auto-applied when no interactive gate is pending, and otherwise returned as needing confirmation.
    /// Not a transaction — environments succeed or fail independently. See docs/20-pipelines-deployments.md.
    /// </summary>
    Task<FanOutResult> FanOutAsync(
        Guid versionId, IReadOnlyList<Guid> environmentIds, CancellationToken ct = default);
}
