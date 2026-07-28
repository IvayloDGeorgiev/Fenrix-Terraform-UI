using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Deployments;
using Fenrix.IaCStudio.Contracts.Deployments;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Domain.Versioning;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Terraform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Deployments;

/// <summary>
/// The deployments engine: read-only board + version-matrix views over existing plan/apply + Git history, and
/// the governed deploy flow (plan → gates → apply the exact saved plan, ADR-0003) with promote, rollback, and
/// fan-out. Reuses the Phase 4 plan/apply spine; the <see cref="IDeploymentRecorder"/> (invoked inside the
/// apply service) writes the <c>Deployment</c> record. Nothing here bypasses the safety gates.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class DeploymentService(
    AppDbContext db,
    IProjectService projects,
    IProjectVersionService versions,
    IPipelineService pipelines,
    ITerraformPlanService planService,
    ITerraformApplyService applyService,
    IGitService git,
    IEnvironmentLockService locks,
    ILogger<DeploymentService> logger) : IDeploymentService
{
    private readonly AppDbContext _db = db;
    private readonly IProjectService _projects = projects;
    private readonly IProjectVersionService _versions = versions;
    private readonly IPipelineService _pipelines = pipelines;
    private readonly ITerraformPlanService _planService = planService;
    private readonly ITerraformApplyService _applyService = applyService;
    private readonly IGitService _git = git;
    private readonly IEnvironmentLockService _locks = locks;
    private readonly ILogger<DeploymentService> _logger = logger;

    // ---- read models ----

    public async Task<IReadOnlyList<DeploymentSummary>> GetDeploymentsAsync(
        Guid projectId, Guid? environmentId = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _db.Deployments.AsNoTracking().Where(d => d.ProjectId == projectId);
        if (environmentId is not null)
            query = query.Where(d => d.EnvironmentId == environmentId);

        var rows = await query
            .OrderByDescending(d => d.StartedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        return rows.Select(MapDeployment).ToList();
    }

    public async Task<DeploymentBoard> GetBoardAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return new DeploymentBoard(projectId, [], []);

        var envs = project.Environments
            .OrderBy(e => e.DisplayOrder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var deployments = await _db.Deployments.AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(ct);
        var versionRows = await _db.ProjectVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId)
            .ToListAsync(ct);
        var versionById = versionRows.ToDictionary(v => v.Id);

        var locksDir = TerraformIntegrity.LocksDirectory(project);
        var stages = new List<DeploymentBoardStage>(envs.Count);
        DeploymentSummary? previousCurrent = null;

        for (var i = 0; i < envs.Count; i++)
        {
            var env = envs[i];
            var last = deployments
                .Where(d => d.EnvironmentId == env.Id && d.Status == DeploymentStatus.Succeeded)
                .OrderByDescending(d => d.CompletedAt ?? d.StartedAt)
                .FirstOrDefault();

            ProjectVersionSummary? current = null;
            if (last is not null && versionById.TryGetValue(last.ProjectVersionId, out var v))
                current = ProjectVersionService.Map(v);

            var active = _locks.GetActive(env.Id, locksDir);
            var isLocked = active is { IsStale: false };
            var lockDetail = isLocked ? $"{active!.Operation} (pid {active.ProcessId})" : null;

            int? behind = null;
            if (i > 0 && previousCurrent is not null && last is not null &&
                !string.IsNullOrEmpty(previousCurrent.GitCommit) && !string.IsNullOrEmpty(last.GitCommit))
            {
                behind = await CommitsBehindAsync(projectId, previousCurrent.GitCommit, last.GitCommit, ct);
            }

            stages.Add(new DeploymentBoardStage(
                env.Id, env.Name, env.IsProduction, i,
                current,
                last is null ? null : MapDeployment(last),
                env.CloudConnectionId is not null,
                isLocked, lockDetail, behind));

            previousCurrent = last is null ? previousCurrent : MapDeployment(last);
        }

        var recent = deployments
            .OrderByDescending(d => d.StartedAt)
            .Take(20)
            .Select(MapDeployment)
            .ToList();

        return new DeploymentBoard(projectId, stages, recent);
    }

    public async Task<VersionMatrix> GetMatrixAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return new VersionMatrix(projectId, [], []);

        var matrixEnvs = project.Environments
            .OrderBy(e => e.DisplayOrder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .Select((e, idx) => new MatrixEnvironment(e.Id, e.Name, e.IsProduction, idx, e.CloudConnectionId is not null))
            .ToList();

        var versionSummaries = await _versions.ListAsync(projectId, ct);
        var deployments = (await _db.Deployments.AsNoTracking()
            .Where(d => d.ProjectId == projectId)
            .ToListAsync(ct))
            .Select(MapDeployment)
            .ToList();

        return VersionMatrixBuilder.Build(projectId, matrixEnvs, versionSummaries, deployments);
    }

    // ---- governed deploy ----

    public async Task<DeployPreparation> PrepareDeployAsync(
        Guid versionId, Guid environmentId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        var version = await _db.ProjectVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return DeployPreparation.Blocked(Guid.Empty, versionId, "", environmentId, "", false, "", "Version not found.");

        var project = await _projects.GetAsync(version.ProjectId, ct);
        var env = project?.Environments.FirstOrDefault(e => e.Id == environmentId);
        if (project is null || env is null)
            return DeployPreparation.Blocked(version.ProjectId, versionId, version.Label, environmentId, "", false, version.GitCommit, "Environment not found.");

        // Current repository state.
        var prov = await SafeProvenanceAsync(project, ct);
        var atVersion = prov.CommitSha is not null &&
                        string.Equals(prov.CommitSha, version.GitCommit, StringComparison.OrdinalIgnoreCase);
        var canCheckout = prov.IsRepository && !prov.IsDirty && !string.IsNullOrEmpty(version.GitCommit);

        var stage = await ResolveStageAsync(project, env, ct);
        var previousStageHasVersion = await ResolvePreviousStageHasVersionAsync(project, env, versionId, stage, ct);

        Guid? savedPlanId = null;
        PlanReview? review = null;
        CommandPreview? planPreview = null;
        string? blockReason = null;

        // Only run the governed plan when the repository is actually at the version's commit — otherwise we'd
        // plan the wrong configuration. The failing RepositoryAtVersion gate + CanCheckout tells the UI to
        // check the version out first, then re-prepare.
        if (atVersion)
        {
            var context = await _planService.PreparePlanAsync(project.Id, env.Id, new PlanOptions(), ct);
            if (!context.CanRun)
            {
                blockReason = context.BlockReason;
            }
            else
            {
                planPreview = context.Preview;
                var result = await _planService.CreatePlanAsync(context, output, ct);
                if (result.Succeeded)
                {
                    savedPlanId = result.SavedPlanId;
                    review = result.Review;
                }
                else
                {
                    blockReason = result.BlockReason ?? "The plan did not complete.";
                }
            }
        }

        var gateResult = EvaluateGates(env, version, prov, stage, previousStageHasVersion);
        var confirmationPhrase = gateResult.RequiresTypedConfirmation ? env.Name : null;

        return new DeployPreparation(
            project.Id, versionId, version.Label, env.Id, env.Name, env.IsProduction,
            version.GitCommit, atVersion, canCheckout,
            savedPlanId, review, planPreview,
            gateResult.Gates, gateResult.RequiresApproval, gateResult.RequiresTypedConfirmation,
            confirmationPhrase, blockReason);
    }

    public async Task<(bool Ok, string? Detail)> CheckoutVersionAsync(Guid versionId, CancellationToken ct = default)
    {
        var version = await _db.ProjectVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null) return (false, "Version not found.");

        var project = await _projects.GetAsync(version.ProjectId, ct);
        if (project is null) return (false, "Project not found.");

        var prov = await SafeProvenanceAsync(project, ct);
        if (!prov.IsRepository) return (false, "The project is not a Git repository.");
        if (prov.IsDirty) return (false, "The working tree has uncommitted changes; commit or stash them first.");

        var target = version.GitTag ?? version.GitCommit;
        if (string.IsNullOrEmpty(target)) return (false, "This version has no Git anchor to check out.");

        var result = await _git.CheckoutAsync(version.ProjectId, target, ct);
        return (result.Succeeded, result.Succeeded ? $"Checked out {target}." : Reason(result));
    }

    public async Task<DeployExecutionResult> ExecuteDeployAsync(
        Guid savedPlanId,
        DeployConfirmation confirmation,
        IProgress<ProcessOutputEvent>? rawOutput,
        IProgress<ApplyProgressEvent>? progress,
        CancellationToken ct = default)
    {
        var plan = await _db.SavedPlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == savedPlanId, ct);
        if (plan is null)
            return DeployExecutionResult.Fail("Saved plan not found.");

        var project = await _projects.GetAsync(plan.ProjectId, ct);
        var env = project?.Environments.FirstOrDefault(e => e.Id == plan.EnvironmentId);
        if (project is null || env is null)
            return DeployExecutionResult.Fail("Environment not found.");

        var version = await _db.ProjectVersions.AsNoTracking()
            .Where(v => v.ProjectId == plan.ProjectId && v.GitCommit == (plan.GitCommitSha ?? ""))
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync(ct);

        // Re-evaluate the pipeline gates (branch / clean tree / previous stage / cloud / repo-at-version). The
        // apply service independently enforces cloud + integrity + production typed-confirm, but we gate the
        // pipeline-specific rules here so a stale preparation can't slip a blocked deploy through.
        var stage = await ResolveStageAsync(project, env, ct);
        var prov = await SafeProvenanceAsync(project, ct);
        var previousStageHasVersion = version is null
            ? (bool?)null
            : await ResolvePreviousStageHasVersionAsync(project, env, version.Id, stage, ct);
        var gateResult = EvaluateGatesForPlan(env, plan, prov, stage, previousStageHasVersion);

        var failing = gateResult.Gates.FirstOrDefault(g => g.IsBlocker && !g.Passed);
        if (failing is not null)
            return DeployExecutionResult.Fail(failing.Detail ?? $"Blocked: {failing.Label}.");

        if (gateResult.RequiresApproval && !confirmation.Approved)
            return DeployExecutionResult.Fail("This stage requires approval before deploying.");

        try
        {
            var apply = await _applyService.ApplyAsync(
                savedPlanId, new ApplyConfirmation(confirmation.TypedValue ?? string.Empty), rawOutput, progress, ct);

            if (!apply.Process.Succeeded)
            {
                return apply.Process.Cancelled
                    ? DeployExecutionResult.Fail("Deployment cancelled.")
                    : DeployExecutionResult.Fail($"Apply failed (exit {apply.Process.ExitCode}).");
            }

            // The recorder (inside the apply service) wrote the Deployment; find it for the result.
            var deployment = await _db.Deployments.AsNoTracking()
                .FirstOrDefaultAsync(d => d.PlanId == savedPlanId, ct);
            return new DeployExecutionResult(true, deployment?.Id, apply, null);
        }
        catch (Exception ex)
        {
            return DeployExecutionResult.Fail(ex.Message);
        }
    }

    // ---- promote / rollback resolution ----

    public async Task<Guid?> GetCurrentVersionIdAsync(Guid environmentId, CancellationToken ct = default)
    {
        var last = await _db.Deployments.AsNoTracking()
            .Where(d => d.EnvironmentId == environmentId && d.Status == DeploymentStatus.Succeeded)
            .OrderByDescending(d => d.StartedAt)
            .FirstOrDefaultAsync(ct);
        return last?.ProjectVersionId;
    }

    public async Task<Guid?> GetRollbackVersionIdAsync(Guid environmentId, CancellationToken ct = default)
    {
        var succeeded = await _db.Deployments.AsNoTracking()
            .Where(d => d.EnvironmentId == environmentId && d.Status == DeploymentStatus.Succeeded)
            .OrderByDescending(d => d.StartedAt)
            .Select(d => new { d.ProjectVersionId, d.StartedAt })
            .ToListAsync(ct);

        // The previous distinct version behind the current one.
        var currentVersion = succeeded.FirstOrDefault()?.ProjectVersionId;
        if (currentVersion is null) return null;

        foreach (var d in succeeded)
        {
            if (d.ProjectVersionId != currentVersion)
                return d.ProjectVersionId;
        }
        return null;
    }

    // ---- fan-out ----

    public async Task<FanOutResult> FanOutAsync(
        Guid versionId, IReadOnlyList<Guid> environmentIds, CancellationToken ct = default)
    {
        var version = await _db.ProjectVersions.AsNoTracking().FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return new FanOutResult(versionId, []);

        var project = await _projects.GetAsync(version.ProjectId, ct);
        if (project is null)
            return new FanOutResult(versionId, []);

        // Ensure the working tree is at the version's commit once for the whole fan-out (clean trees only).
        var prov = await SafeProvenanceAsync(project, ct);
        if (prov.IsRepository && !string.IsNullOrEmpty(version.GitCommit) &&
            !string.Equals(prov.CommitSha, version.GitCommit, StringComparison.OrdinalIgnoreCase) && !prov.IsDirty)
        {
            await CheckoutVersionAsync(versionId, ct);
        }

        var items = new List<FanOutItemResult>();
        foreach (var envId in environmentIds)
        {
            var env = project.Environments.FirstOrDefault(e => e.Id == envId);
            var envName = env?.Name ?? envId.ToString();
            try
            {
                var prep = await PrepareDeployAsync(versionId, envId, null, ct);
                if (prep.BlockReason is not null)
                {
                    items.Add(new FanOutItemResult(envId, envName, FanOutOutcome.Blocked, null, null, prep.BlockReason));
                    continue;
                }
                if (!prep.PlanReady || prep.SavedPlanId is null)
                {
                    var reason = prep.Gates.FirstOrDefault(g => g.IsBlocker && !g.Passed)?.Detail
                                 ?? "A blocking gate did not pass.";
                    items.Add(new FanOutItemResult(envId, envName, FanOutOutcome.Blocked, null, prep.SavedPlanId, reason));
                    continue;
                }
                if (prep.RequiresApproval || prep.RequiresTypedConfirmation)
                {
                    items.Add(new FanOutItemResult(envId, envName, FanOutOutcome.NeedsConfirmation, null, prep.SavedPlanId,
                        prep.RequiresTypedConfirmation ? "Production confirmation required." : "Approval required."));
                    continue;
                }

                var exec = await ExecuteDeployAsync(prep.SavedPlanId.Value, new DeployConfirmation(true, null), null, null, ct);
                items.Add(exec.Succeeded
                    ? new FanOutItemResult(envId, envName, FanOutOutcome.Deployed, exec.DeploymentId, prep.SavedPlanId, null)
                    : new FanOutItemResult(envId, envName, FanOutOutcome.Failed, null, prep.SavedPlanId, exec.Error));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Fan-out to {Env} failed.", envName);
                items.Add(new FanOutItemResult(envId, envName, FanOutOutcome.Failed, null, null, ex.Message));
            }
        }

        return new FanOutResult(versionId, items);
    }

    // ---- gate helpers ----

    private DeploymentGateEvaluator.Result EvaluateGates(
        ProjectEnvironment env, ProjectVersion version, GitProvenance prov,
        StageRules stage, bool? previousStageHasVersion)
    {
        var inputs = new DeploymentGateEvaluator.GateInputs(
            HasCloudConnection: env.CloudConnectionId is not null,
            IsProduction: env.IsProduction,
            VersionCommit: version.GitCommit,
            CurrentCommit: prov.CommitSha,
            CurrentBranch: prov.Branch,
            WorkingTreeDirty: prov.IsDirty,
            RequireApproval: stage.RequireApproval,
            RequirePreviousStageSuccess: stage.RequirePreviousStageSuccess,
            RequireCleanWorkingTree: stage.RequireCleanWorkingTree,
            RequireTypedConfirmationForProduction: stage.RequireTypedConfirmationForProduction,
            RequiredBranch: stage.RequiredBranch,
            PreviousStageHasVersion: previousStageHasVersion);
        return DeploymentGateEvaluator.Evaluate(inputs);
    }

    private DeploymentGateEvaluator.Result EvaluateGatesForPlan(
        ProjectEnvironment env, SavedPlan plan, GitProvenance prov,
        StageRules stage, bool? previousStageHasVersion)
    {
        var inputs = new DeploymentGateEvaluator.GateInputs(
            HasCloudConnection: env.CloudConnectionId is not null,
            IsProduction: env.IsProduction,
            VersionCommit: plan.GitCommitSha ?? prov.CommitSha ?? string.Empty,
            CurrentCommit: prov.CommitSha,
            CurrentBranch: prov.Branch,
            WorkingTreeDirty: prov.IsDirty,
            RequireApproval: stage.RequireApproval,
            RequirePreviousStageSuccess: stage.RequirePreviousStageSuccess,
            RequireCleanWorkingTree: stage.RequireCleanWorkingTree,
            RequireTypedConfirmationForProduction: stage.RequireTypedConfirmationForProduction,
            RequiredBranch: stage.RequiredBranch,
            PreviousStageHasVersion: previousStageHasVersion);
        return DeploymentGateEvaluator.Evaluate(inputs);
    }

    /// <summary>The effective gate rules for an environment: from the pipeline stage if defined, else defaults.</summary>
    private async Task<StageRules> ResolveStageAsync(InfrastructureProject project, ProjectEnvironment env, CancellationToken ct)
    {
        var pipeline = await _pipelines.GetAsync(project.Id, ct);
        var stage = pipeline?.Stages.FirstOrDefault(s => s.EnvironmentId == env.Id);
        if (stage is not null)
            return new StageRules(
                stage.Order, stage.RequireApproval, stage.RequirePreviousStageSuccess,
                stage.RequireCleanWorkingTree, stage.RequireTypedConfirmationForProduction,
                stage.RequiredBranch, pipeline);

        // No pipeline stage → sensible default: production requires typed-confirm; nothing else gated.
        return new StageRules(-1, false, false, false, true, null, pipeline);
    }

    /// <summary>Whether the stage immediately upstream of this one currently holds the given version.</summary>
    private async Task<bool?> ResolvePreviousStageHasVersionAsync(
        InfrastructureProject project, ProjectEnvironment env, Guid versionId, StageRules stage, CancellationToken ct)
    {
        if (!stage.RequirePreviousStageSuccess || stage.Pipeline is null || stage.Order <= 0)
            return null; // no upstream stage → gate passes by default

        var upstream = stage.Pipeline.Stages
            .Where(s => s.Order < stage.Order)
            .OrderByDescending(s => s.Order)
            .FirstOrDefault();
        if (upstream is null)
            return null;

        var currentUpstream = await GetCurrentVersionIdAsync(upstream.EnvironmentId, ct);
        return currentUpstream == versionId;
    }

    /// <summary>Best-effort "N commits behind" using recent history (both commits on the same linear history).</summary>
    private async Task<int?> CommitsBehindAsync(Guid projectId, string upstreamCommit, string downstreamCommit, CancellationToken ct)
    {
        if (string.Equals(upstreamCommit, downstreamCommit, StringComparison.OrdinalIgnoreCase))
            return 0;
        try
        {
            var history = await _git.GetHistoryAsync(projectId, 200, null, ct);
            var up = IndexOf(history, upstreamCommit);
            var down = IndexOf(history, downstreamCommit);
            if (up < 0 || down < 0) return null;
            var behind = down - up; // newest-first: downstream older ⇒ larger index
            return behind > 0 ? behind : null;
        }
        catch
        {
            return null;
        }
    }

    private static int IndexOf(IReadOnlyList<GitCommit> history, string commit)
    {
        for (var i = 0; i < history.Count; i++)
            if (history[i].Sha.StartsWith(commit, StringComparison.OrdinalIgnoreCase) ||
                commit.StartsWith(history[i].Sha, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    private async Task<GitProvenance> SafeProvenanceAsync(InfrastructureProject project, CancellationToken ct)
    {
        try { return await _git.ReadProvenanceAsync(project.RepositoryRootPath ?? project.RootPath, ct); }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read Git provenance for {Project}.", project.Name);
            return GitProvenance.None;
        }
    }

    private static DeploymentSummary MapDeployment(Deployment d) => new(
        d.Id, d.ProjectId, d.EnvironmentId, d.ProjectVersionId, d.PlanId,
        d.VersionLabel, d.GitCommit, Short(d.GitCommit), d.GitBranch, d.TerraformVersion,
        d.StateBackend, d.StateSerial, d.StateLineage,
        d.Status, d.StartedAt, d.CompletedAt, d.InitiatedBy,
        d.AddCount, d.ChangeCount, d.DestroyCount, d.ReplaceCount);

    private static string Short(string sha) => string.IsNullOrEmpty(sha) ? "" : sha.Length > 7 ? sha[..7] : sha;

    private static string Reason(GitOperationResult r) =>
        !string.IsNullOrWhiteSpace(r.Error) ? r.Error!
        : !string.IsNullOrWhiteSpace(r.Output) ? r.Output
        : "unknown error";

    /// <summary>The resolved gate rules for one environment + the pipeline they came from (for upstream lookup).</summary>
    private readonly record struct StageRules(
        int Order,
        bool RequireApproval,
        bool RequirePreviousStageSuccess,
        bool RequireCleanWorkingTree,
        bool RequireTypedConfirmationForProduction,
        string? RequiredBranch,
        PipelineDefinition? Pipeline);
}
