using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Persists saved-plan safety records (metadata + integrity hashes + redacted counts). No raw plan JSON is
/// ever stored. See docs/06-plan-apply-safety.md and docs/12-database-design.md.
/// </summary>
public interface ISavedPlanStore
{
    Task<SavedPlan> AddAsync(SavedPlan plan, CancellationToken ct = default);

    Task<SavedPlan?> GetAsync(Guid id, CancellationToken ct = default);

    Task UpdateAsync(SavedPlan plan, CancellationToken ct = default);

    /// <summary>Most recent saved plans for a project (optionally one environment), newest first.</summary>
    Task<IReadOnlyList<SavedPlanSummary>> GetRecentAsync(
        Guid projectId, Guid? environmentId = null, int limit = 25, CancellationToken ct = default);
}
