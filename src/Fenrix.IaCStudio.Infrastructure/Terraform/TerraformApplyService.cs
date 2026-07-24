using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Files;
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

        // Warnings (non-blocking). Git branch/uncommitted warnings arrive in Phase 5.
        if (plan.HasDeletions)
            checks.Add(new PreflightCheck($"{plan.DestroyCount} resource(s) will be destroyed", false, PreflightSeverity.Warning));
        if (plan.HasReplacements)
            checks.Add(new PreflightCheck($"{plan.ReplaceCount} resource(s) will be replaced", false, PreflightSeverity.Warning));
        if (plan.IsProductionTarget)
            checks.Add(new PreflightCheck("This targets a PRODUCTION environment", false, PreflightSeverity.Warning));

        var preview = BuildApplyPreview(plan, environment, installation);
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
        var applyRequest = CommandPreviewBuilder.BuildRequest(applySpec, exePath, plan.WorkingDirectory);

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

        return new ApplyResult(savedPlanId, applyRun.RunId, applyRun.Process, added, changed, destroyed, applyRun.LogPath);
    }

    // ---- helpers ----

    private CommandPreview BuildApplyPreview(SavedPlan plan, ProjectEnvironment? environment, TerraformInstallation? installation)
    {
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var applySpec = new TerraformRunSpec(plan.ProjectId, plan.EnvironmentId, TerraformCommandKind.Apply)
        {
            PlanFilePath = plan.PlanFilePath
        };
        var request = CommandPreviewBuilder.BuildRequest(applySpec, exePath, plan.WorkingDirectory);

        var risk = plan.HasDeletions || plan.HasReplacements ? "destructive" : "state-changing";
        var chips = new List<CommandContextChip>
        {
            new("Terraform", installation?.Version?.ToString() ?? plan.TerraformVersion ?? "not found"),
            new("Environment", environment?.Name ?? plan.EnvironmentName),
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
