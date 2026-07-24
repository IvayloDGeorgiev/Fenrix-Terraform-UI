using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// EF Core store for <see cref="SavedPlan"/> safety records. Persists redacted counts, integrity hashes,
/// and provenance only — never raw plan JSON. Provider-neutral (SQLite/SQL Server). See
/// docs/06-plan-apply-safety.md and docs/12-database-design.md.
/// </summary>
public sealed class EfSavedPlanStore(AppDbContext db) : ISavedPlanStore
{
    private readonly AppDbContext _db = db;

    public async Task<SavedPlan> AddAsync(SavedPlan plan, CancellationToken ct = default)
    {
        _db.SavedPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return plan;
    }

    public Task<SavedPlan?> GetAsync(Guid id, CancellationToken ct = default) =>
        _db.SavedPlans.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task UpdateAsync(SavedPlan plan, CancellationToken ct = default)
    {
        _db.SavedPlans.Update(plan);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<SavedPlanSummary>> GetRecentAsync(
        Guid projectId, Guid? environmentId = null, int limit = 25, CancellationToken ct = default)
    {
        var query = _db.SavedPlans.AsNoTracking().Where(p => p.ProjectId == projectId);
        if (environmentId is not null)
            query = query.Where(p => p.EnvironmentId == environmentId);

        var rows = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

        return rows.Select(p => new SavedPlanSummary(
            p.Id, p.ProjectId, p.EnvironmentId, p.EnvironmentName, p.Mode,
            p.PlanFilePath, p.RelativePlanFilePath, p.TerraformVersion,
            p.AddCount, p.ChangeCount, p.DestroyCount, p.ReplaceCount,
            p.IsProductionTarget, p.Applied, p.CreatedAt, p.AppliedAt,
            p.IsInvalidated, p.InvalidatedReason)).ToList();
    }
}
