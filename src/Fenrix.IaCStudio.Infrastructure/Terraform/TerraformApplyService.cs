using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Files;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Files;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Applies the exact saved plan (ADR-0003): evaluates the safety gates, enforces the typed production
/// confirmation, acquires the per-environment lock, runs <c>apply -json</c> while streaming structured
/// per-resource progress, marks the plan applied, and version-controls the resulting state file. See
/// docs/06-plan-apply-safety.md and docs/25-execution-lifecycle.md.
/// </summary>
public sealed class TerraformApplyService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    ISavedPlanStore plans,
    IEnvironmentLockService locks,
    IFileHistoryStore fileHistory,
    IGitService git,
    ICloudEnvironmentComposer cloud,
    IDeploymentRecorder deployments,
    ILogger<TerraformApplyService> logger) : ITerraformApplyService
{
    private const string DefaultExecutable = "terraform";
    private const string StateFileName = "terraform.tfstate";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly ISavedPlanStore _plans = plans;
    private readonly IEnvironmentLockService _locks = locks;
    private readonly IFileHistoryStore _fileHistory = fileHistory;
    private readonly IGitService _git = git;
    private readonly ICloudEnvironmentComposer _cloud = cloud;
    private readonly IDeploymentRecorder _deployments = deployments;
    private readonly ILogger<TerraformApplyService> _logger = logger;

    public async Task<ApplyPreflight> PreflightAsync(Guid savedPlanId, CancellationToken ct = default)
    {
        var plan = await _plans.GetAsync(savedPlanId, ct);
        if (plan is null)
            return Blocked(savedPlanId, "Saved plan not found.");

        var project = await _projects.GetAsync(plan.ProjectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == plan.EnvironmentId);
        var installation = await _discovery.ResolveAsync(plan.ProjectId, ct);
        var checks = new List<PreflightCheck>();

        // Plan file present + unmodified.
        var planExists = File.Exists(plan.PlanFilePath);
        checks.Add(new PreflightCheck("Saved plan file present", planExists, PreflightSeverity.Blocker,
            planExists ? null : plan.PlanFilePath));

        var hashOk = planExists && plan.PlanFileHash is not null
            && string.Equals(await FileHashing.Sha256HexAsync(plan.PlanFilePath, ct), plan.PlanFileHash, StringComparison.Ordinal);
        checks.Add(new PreflightCheck("Plan file unmodified", hashOk, PreflightSeverity.Blocker,
            hashOk ? null : "The plan file hash does not match the one recorded when it was created."));

        // Not already applied.
        checks.Add(new PreflightCheck("Not already applied", !plan.Applied, PreflightSeverity.Blocker,
            plan.Applied ? $"Applied {plan.AppliedAt?.LocalDateTime:g}." : null));

        // Configuration & lock integrity → invalidation.
        string? invalidation = plan.IsInvalidated ? (plan.InvalidatedReason ?? "This plan was invalidated.") : null;
        if (invalidation is null && project is not null)
        {
            var currentConfig = await TerraformIntegrity.ComputeConfigHashAsync(project.RootPath, plan.WorkingDirectory, ct);
            var currentLock = await TerraformIntegrity.ComputeLockHashAsync(plan.WorkingDirectory, ct);
            invalidation = PlanIntegrity.DetermineInvalidation(plan.ConfigHash, currentConfig, plan.LockHash, currentLock);
            if (invalidation is not null)
            {
                plan.IsInvalidated = true;
                plan.InvalidatedReason = invalidation;
                await _plans.UpdateAsync(plan, ct);
            }
        }
        checks.Add(new PreflightCheck("Configuration & provider lock unchanged", invalidation is null, PreflightSeverity.Blocker, invalidation));

        // Environment present + cloud account unchanged.
        var envOk = environment is not null;
        checks.Add(new PreflightCheck("Target environment exists", envOk, PreflightSeverity.Blocker,
            envOk ? null : "The environment no longer exists on this project."));
        var hasConnection = environment?.CloudConnectionId is not null;
        checks.Add(new PreflightCheck("Environment has a bound cloud connection", hasConnection, PreflightSeverity.Blocker,
            hasConnection ? null : "Bind a cloud connection to this environment before applying (authentication required)."));
        var cloudOk = environment is not null && environment.CloudConnectionId == plan.CloudConnectionId;
        checks.Add(new PreflightCheck("Environment cloud account unchanged", cloudOk, PreflightSeverity.Blocker,
            cloudOk ? null : "The environment's bound cloud connection changed since the plan was created."));

        // Terraform binary available + version compatible.
        var versionOk = installation is not null && installation.SatisfiesConstraint(project?.RequiredTerraformVersion);
        checks.Add(new PreflightCheck("Terraform binary available & compatible", versionOk, PreflightSeverity.Blocker,
            versionOk ? null : "No compatible Terraform binary was resolved for this project."));

        // Environment lock free.
        var locksDir = project is not null ? TerraformIntegrity.LocksDirectory(project) : string.Empty;
        var active = environment is not null && project is not null ? _locks.GetActive(environment.Id, locksDir) : null;
        var lockFree = active is null || active.IsStale;
        checks.Add(new PreflightCheck("Environment not locked", lockFree, PreflightSeverity.Blocker,
            lockFree ? null : $"Locked by a {active!.Operation} operation (pid {active.ProcessId})."));

        // Warnings (non-blocking).
        if (plan.HasDeletions)
            checks.Add(new PreflightCheck($"{plan.DestroyCount} resource(s) will be destroyed", false, PreflightSeverity.Warning));
        if (plan.HasReplacements)
            checks.Add(new PreflightCheck($"{plan.ReplaceCount} resource(s) will be replaced", false, PreflightSeverity.Warning));
        if (plan.IsProductionTarget)
            checks.Add(new PreflightCheck("This targets a PRODUCTION environment", false, PreflightSeverity.Warning));

        // Git provenance warnings (Phase 5): the branch/HEAD moved or the tree is dirty since the plan was
        // reviewed. Non-blocking — the saved plan still applies exactly, but the reviewer should know the
        // repository no longer matches what they looked at (docs/06-plan-apply-safety.md, docs/08-git-engine.md).
        await AddGitProvenanceWarningsAsync(plan, checks, ct);

        var cloudEnv = await _cloud.ComposeAsync(environment?.CloudConnectionId, ct);
        var preview = BuildApplyPreview(plan, environment, installation, cloudEnv);
        var canApply = checks.Where(c => c.Severity == PreflightSeverity.Blocker).All(c => c.Passed);
        var requiresTyped = environment?.IsProduction ?? plan.IsProductionTarget;

        return new ApplyPreflight(savedPlanId, canApply, requiresTyped, requiresTyped ? environment?.Name ?? plan.EnvironmentName : null, preview, checks);
    }

    public async Task<ApplyResult> ApplyAsync(
        Guid savedPlanId,
        ApplyConfirmation confirmation,
        IProgress<ProcessOutputEvent>? rawOutput,
        IProgress<ApplyProgressEvent>? progress,
        CancellationToken ct = default)
    {
        var preflight = await PreflightAsync(savedPlanId, ct);
        if (!preflight.CanApply)
        {
            var reason = preflight.Blockers.FirstOrDefault()?.Detail ?? "Apply is blocked by a failed safety check.";
            throw new InvalidOperationException(reason);
        }

        if (preflight.RequiresTypedConfirmation &&
            !string.Equals(confirmation.TypedValue?.Trim(), preflight.ConfirmationPhrase, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Type '{preflight.ConfirmationPhrase}' to confirm this production deployment.");
        }

        var plan = await _plans.GetAsync(savedPlanId, ct)
            ?? throw new InvalidOperationException("Saved plan not found.");
        var project = await _projects.GetAsync(plan.ProjectId, ct)
            ?? throw new InvalidOperationException("Project not found.");
        var environment = project.Environments.FirstOrDefault(e => e.Id == plan.EnvironmentId)
            ?? throw new InvalidOperationException("Environment not found.");
        var installation = await _discovery.ResolveAsync(plan.ProjectId, ct);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;

        var locksDir = TerraformIntegrity.LocksDirectory(project);
        await using var envLock = await _locks.TryAcquireAsync(new EnvironmentLockRequest(environment.Id, locksDir, "apply"), ct);
        if (envLock is null)
            throw new InvalidOperationException("The environment is locked by another operation.");

        // Re-verify the plan file hash immediately before executing (guards against a swap after preflight).
        if (plan.PlanFileHash is not null &&
            !string.Equals(await FileHashing.Sha256HexAsync(plan.PlanFilePath, ct), plan.PlanFileHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The plan file changed after preflight; refusing to apply.");
        }

        var applySpec = new TerraformRunSpec(plan.ProjectId, plan.EnvironmentId, TerraformCommandKind.Apply)
        {
            PlanFilePath = plan.PlanFilePath
        };
        // Compose the environment's bound cloud credentials just-in-time; they live only in the child
        // process env (secret values are redacted in the preview/history by ArgumentRedactor).
        var cloudEnv = await _cloud.ComposeAsync(environment.CloudConnectionId, ct);
        var applyRequest = CommandPreviewBuilder.BuildRequest(applySpec, exePath, plan.WorkingDirectory, cloudEnv.EnvironmentVariables);

        ApplyJsonParser.ApplyChangeCounts? summary = null;
        var forwarder = new ApplyProgressForwarder(rawOutput, progress, c => summary = c);

        // apply -json output can carry sensitive output values → not persisted to a log file.
        var applyRun = await _coordinator.RunAsync(applyRequest, forwarder, captureLog: false, ct);

        if (applyRun.Process.Succeeded)
        {
            plan.Applied = true;
            plan.AppliedAt = DateTimeOffset.UtcNow;
            plan.ApplyCommandRunId = applyRun.RunId;
            await _plans.UpdateAsync(plan, ct);

            // Version-control the resulting local state file (if a local backend is in use).
            await CaptureStateInHistoryAsync(project, plan.WorkingDirectory, ct);
        }

        var added = summary?.Add ?? plan.AddCount;
        var changed = summary?.Change ?? plan.ChangeCount;
        var destroyed = summary?.Remove ?? plan.DestroyCount;

        _logger.LogInformation("Applied plan {PlanId} for {Project}/{Env}: {Status} (+{Add} ~{Change} -{Destroy})",
            plan.Id, project.Name, environment.Name,
            applyRun.Process.Succeeded ? "succeeded" : applyRun.Process.Cancelled ? "cancelled" : "failed",
            added, changed, destroyed);

        var result = new ApplyResult(savedPlanId, applyRun.RunId, applyRun.Process, added, changed, destroyed, applyRun.LogPath);

        // Record a Deployment for the board on success — covers both the Plan & apply page and the governed
        // Pipelines flow (this is the single apply entry point). Best-effort; never fails the succeeded apply.
        if (applyRun.Process.Succeeded)
            await _deployments.RecordApplyAsync(plan, result, ct);

        return result;
    }

    // ---- helpers ----

    /// <summary>
    /// Compares the plan's recorded Git provenance to the current working tree and appends non-blocking
    /// warnings when the branch changed, HEAD moved, or there are uncommitted changes. Only runs when the
    /// plan captured provenance (the project is a repository). See docs/08-git-engine.md.
    /// </summary>
    private async Task AddGitProvenanceWarningsAsync(SavedPlan plan, List<PreflightCheck> checks, CancellationToken ct)
    {
        // No recorded provenance → the project wasn't a repo at plan time; nothing to compare.
        if (plan.GitCommitSha is null && plan.GitBranch is null && plan.GitTreeDirty is null)
            return;

        GitProvenance current;
        try
        {
            current = await _git.ReadProvenanceAsync(plan.WorkingDirectory, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Git provenance for apply preflight of plan {PlanId}.", plan.Id);
            return;
        }

        if (!current.IsRepository)
            return;

        if (plan.GitBranch is not null && current.Branch is not null &&
            !string.Equals(plan.GitBranch, current.Branch, StringComparison.Ordinal))
        {
            checks.Add(new PreflightCheck("Branch changed since the plan was created", false, PreflightSeverity.Warning,
                $"Planned on '{plan.GitBranch}', now on '{current.Branch}'."));
        }

        if (plan.GitCommitSha is not null && current.CommitSha is not null &&
            !string.Equals(plan.GitCommitSha, current.CommitSha, StringComparison.Ordinal))
        {
            checks.Add(new PreflightCheck("HEAD moved since the plan was created", false, PreflightSeverity.Warning,
                $"Planned at {Short(plan.GitCommitSha)}, now at {Short(current.CommitSha)}."));
        }

        if (current.IsDirty)
        {
            checks.Add(new PreflightCheck("Uncommitted changes in the working tree", false, PreflightSeverity.Warning,
                "The repository has changes that are not committed."));
        }

        static string Short(string sha) => sha.Length > 8 ? sha[..8] : sha;
    }

    private CommandPreview BuildApplyPreview(
        SavedPlan plan, ProjectEnvironment? environment, TerraformInstallation? installation,
        Fenrix.IaCStudio.Contracts.Cloud.CloudEnvironmentResult cloudEnv)
    {
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var applySpec = new TerraformRunSpec(plan.ProjectId, plan.EnvironmentId, TerraformCommandKind.Apply)
        {
            PlanFilePath = plan.PlanFilePath
        };
        var request = CommandPreviewBuilder.BuildRequest(applySpec, exePath, plan.WorkingDirectory, cloudEnv.EnvironmentVariables);

        var risk = plan.HasDeletions || plan.HasReplacements ? "destructive" : "state-changing";
        var chips = new List<CommandContextChip>
        {
            new("Terraform", installation?.Version?.ToString() ?? plan.TerraformVersion ?? "not found"),
            new("Environment", environment?.Name ?? plan.EnvironmentName),
            new("Cloud", cloudEnv.HasConnection ? cloudEnv.IdentityChip! : "none — bind a connection"),
            new("Risk", risk)
        };
        if (plan.IsProductionTarget)
            chips.Add(new CommandContextChip("Target", "production"));

        return CommandPreviewBuilder.BuildPreview(request, chips);
    }

    private async Task CaptureStateInHistoryAsync(InfrastructureProject project, string workingDir, CancellationToken ct)
    {
        try
        {
            var statePath = Path.Combine(workingDir, StateFileName);
            if (!File.Exists(statePath))
                return; // remote backend → no local state file to version

            var rel = FileTrackingPolicy.ToRelative(project.RootPath, statePath);
            await _fileHistory.RecordAsync(new FileChange
            {
                ProjectId = project.Id,
                RelativePath = rel,
                FullPath = statePath,
                ChangeKind = FileChangeKind.Updated,
                Origin = ChangeOrigin.External
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not capture Terraform state in file history.");
        }
    }

    private static ApplyPreflight Blocked(Guid savedPlanId, string reason) =>
        new(savedPlanId, false, false, null, null,
            [new PreflightCheck(reason, false, PreflightSeverity.Blocker)]);

    /// <summary>
    /// Fans each apply output line out to the raw console, the structured per-resource view (parsed from
    /// <c>-json</c>), and the change-summary capture. See docs/25-execution-lifecycle.md.
    /// </summary>
    private sealed class ApplyProgressForwarder(
        IProgress<ProcessOutputEvent>? raw,
        IProgress<ApplyProgressEvent>? structured,
        Action<ApplyJsonParser.ApplyChangeCounts> onSummary) : IProgress<ProcessOutputEvent>
    {
        public void Report(ProcessOutputEvent value)
        {
            raw?.Report(value);

            var progress = ApplyJsonParser.TryParseProgress(value.Text);
            if (progress is not null)
                structured?.Report(progress);

            var counts = ApplyJsonParser.TryParseChangeSummary(value.Text);
            if (counts is { Operation: "apply" } c)
                onSummary(c);
        }
    }
}
