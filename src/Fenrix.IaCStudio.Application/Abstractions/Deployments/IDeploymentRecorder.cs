using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Deployments;

/// <summary>
/// Records a <c>Deployment</c> row after a successful apply of a saved plan. The single writer of deployment
/// history, so <em>every</em> successful apply — whether started from the Plan &amp; apply page or the
/// governed Pipelines deploy flow — lands on the board. Resolves (or creates) the matching
/// <see cref="Domain.Versioning.ProjectVersion"/> from the plan's Git commit and reads the post-apply state
/// serial/lineage (read-only, in memory, never logged). Idempotent per saved plan. Kept as a narrow interface
/// (no dependency on the apply service) so the apply service can call it without a DI cycle.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public interface IDeploymentRecorder
{
    /// <summary>
    /// Records a deployment for a successfully applied saved plan, returning the new (or existing) deployment
    /// id. Best-effort: returns null and logs if recording fails — it must never break the apply that succeeded.
    /// </summary>
    Task<Guid?> RecordApplyAsync(SavedPlan plan, ApplyResult result, CancellationToken ct = default);
}
