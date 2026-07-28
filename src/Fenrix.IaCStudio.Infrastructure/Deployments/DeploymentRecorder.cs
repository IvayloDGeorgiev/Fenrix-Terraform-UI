using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Domain.Versioning;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Deployments;

/// <summary>
/// The single writer of <c>Deployment</c> history. Called after any successful apply of a saved plan, so
/// every apply — from the Plan &amp; apply page or the governed Pipelines flow — lands on the board. Resolves
/// (or creates) the <see cref="ProjectVersion"/> matching the plan's Git commit, reads the post-apply state
/// serial/lineage via the read-only inspection path (never logged), and writes the deployment. Idempotent per
/// saved plan and best-effort: a recording failure never breaks the apply that succeeded.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class DeploymentRecorder(
    AppDbContext db,
    IProjectService projects,
    ILogger<DeploymentRecorder> logger) : IDeploymentRecorder
{
    private const string StateFileName = "terraform.tfstate";

    private readonly AppDbContext _db = db;
    private readonly IProjectService _projects = projects;
    private readonly ILogger<DeploymentRecorder> _logger = logger;

    public async Task<Guid?> RecordApplyAsync(SavedPlan plan, ApplyResult result, CancellationToken ct = default)
    {
        try
        {
            // Idempotency: one deployment per applied saved plan.
            var existing = await _db.Deployments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.PlanId == plan.Id, ct);
            if (existing is not null)
                return existing.Id;

            var project = await _projects.GetAsync(plan.ProjectId, ct);
            var environment = project?.Environments.FirstOrDefault(e => e.Id == plan.EnvironmentId);

            var version = await ResolveOrCreateVersionAsync(plan, ct);

            // Post-apply state pointer (serial/lineage). Read straight from the local state file (in memory,
            // only the two non-sensitive top-level fields — never the resource values, never logged). Remote
            // backends have no local file, so this is best-effort and simply stays null there.
            var (serial, lineage) = await ReadStatePointerAsync(plan.WorkingDirectory, ct);

            var deployment = new Deployment
            {
                ProjectId = plan.ProjectId,
                EnvironmentId = plan.EnvironmentId,
                ProjectVersionId = version.Id,
                PlanId = plan.Id,
                VersionLabel = version.Label,
                GitCommit = plan.GitCommitSha ?? version.GitCommit,
                GitBranch = plan.GitBranch ?? version.GitBranch ?? string.Empty,
                ConfigurationHash = plan.ConfigHash ?? version.ConfigurationHash,
                ProviderLockHash = plan.LockHash ?? version.ProviderLockHash,
                TerraformVersion = plan.TerraformVersion ?? string.Empty,
                StateBackend = BackendLabel(environment),
                StateSerial = serial,
                StateLineage = lineage,
                Status = DeploymentStatus.Succeeded,
                StartedAt = plan.AppliedAt ?? DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow,
                InitiatedBy = System.Environment.UserName,
                AddCount = result.Added,
                ChangeCount = result.Changed,
                DestroyCount = result.Destroyed,
                ReplaceCount = plan.ReplaceCount
            };

            _db.Deployments.Add(deployment);
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Recorded deployment of {Label} to {Env} (plan {PlanId}).",
                version.Label, plan.EnvironmentName, plan.Id);
            return deployment.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record a deployment for plan {PlanId}; the apply itself succeeded.", plan.Id);
            return null;
        }
    }

    /// <summary>
    /// Finds the project version whose Git commit matches the plan's; if none exists (e.g. an ad-hoc apply
    /// from the Plan &amp; apply page, or a project with no cut versions yet), creates an implicit version so
    /// the board always has something to point at.
    /// </summary>
    private async Task<ProjectVersion> ResolveOrCreateVersionAsync(SavedPlan plan, CancellationToken ct)
    {
        var commit = plan.GitCommitSha;
        if (!string.IsNullOrEmpty(commit))
        {
            var match = await _db.ProjectVersions
                .Where(v => v.ProjectId == plan.ProjectId && v.GitCommit == commit)
                .OrderByDescending(v => v.CreatedAt)
                .FirstOrDefaultAsync(ct);
            if (match is not null)
                return match;
        }

        var label = !string.IsNullOrEmpty(commit)
            ? Short(commit!)
            : $"apply-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        // Guard against a label collision on the implicit label.
        if (await _db.ProjectVersions.AnyAsync(v => v.ProjectId == plan.ProjectId && v.Label == label, ct))
            label = $"{label}-{Guid.NewGuid().ToString()[..4]}";

        var created = new ProjectVersion
        {
            ProjectId = plan.ProjectId,
            Label = label,
            GitCommit = commit ?? string.Empty,
            GitBranch = plan.GitBranch,
            ConfigurationHash = plan.ConfigHash ?? string.Empty,
            ProviderLockHash = plan.LockHash ?? string.Empty,
            RequiredTerraformVersion = plan.TerraformVersion,
            Notes = "Captured automatically from an apply.",
            CreatedBy = System.Environment.UserName
        };
        _db.ProjectVersions.Add(created);
        await _db.SaveChangesAsync(ct);
        return created;
    }

    /// <summary>
    /// Reads only the top-level <c>serial</c> and <c>lineage</c> of a local <c>terraform.tfstate</c> — the
    /// two non-sensitive state-version fields. Never reads or surfaces resource attribute values.
    /// </summary>
    private async Task<(long? Serial, string? Lineage)> ReadStatePointerAsync(string workingDir, CancellationToken ct)
    {
        try
        {
            var statePath = Path.Combine(workingDir, StateFileName);
            if (!File.Exists(statePath))
                return (null, null);

            await using var stream = File.OpenRead(statePath);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return (null, null);

            long? serial = root.TryGetProperty("serial", out var s) && s.TryGetInt64(out var sv) ? sv : null;
            string? lineage = root.TryGetProperty("lineage", out var l) && l.ValueKind == JsonValueKind.String
                ? l.GetString() : null;
            return (serial, lineage);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read state pointer from {Dir}.", workingDir);
            return (null, null);
        }
    }

    private static string? BackendLabel(ProjectEnvironment? env)
    {
        if (env is null) return null;
        if (!string.IsNullOrWhiteSpace(env.TerraformWorkspace)) return $"workspace:{env.TerraformWorkspace}";
        if (!string.IsNullOrWhiteSpace(env.BackendConfigFile)) return env.BackendConfigFile;
        return null;
    }

    private static string Short(string sha) => sha.Length > 7 ? sha[..7] : sha;
}
