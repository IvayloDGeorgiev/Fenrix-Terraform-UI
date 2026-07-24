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
using Fenrix.IaCStudio.Infrastructure.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Produces saved plans: resolves context and previews the exact <c>plan</c> command, runs it under the
/// per-environment lock, converts it with <c>show -json</c> (parsed in memory, redacted), hashes it for
/// integrity, persists the safety record, and version-controls the plan file via file history. See
/// docs/06-plan-apply-safety.md, docs/23-command-transparency.md, docs/25-execution-lifecycle.md.
/// </summary>
public sealed class TerraformPlanService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    ISavedPlanStore plans,
    IEnvironmentLockService locks,
    IFileHistoryStore fileHistory,
    ILogger<TerraformPlanService> logger) : ITerraformPlanService
{
    private const string DefaultExecutable = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly ISavedPlanStore _plans = plans;
    private readonly IEnvironmentLockService _locks = locks;
    private readonly IFileHistoryStore _fileHistory = fileHistory;
    private readonly ILogger<TerraformPlanService> _logger = logger;

    public async Task<PlanContext> PreparePlanAsync(
        Guid projectId, Guid environmentId, PlanOptions options, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == environmentId);
        var installation = await _discovery.ResolveAsync(projectId, ct);

        var mode = ResolveMode(options);
        var planId = Guid.NewGuid();

        // Without a project we cannot resolve an -out path (the catalog requires one), so return a
        // blocked context with a placeholder preview rather than throwing.
        if (project is null)
        {
            var emptySpec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.Plan) { Plan = options };
            var emptyPreview = new CommandPreview(DefaultExecutable, DefaultExecutable, ["plan"], string.Empty, [], "terraform plan");
            return new PlanContext(projectId, environmentId, planId, mode, string.Empty, null, string.Empty, string.Empty,
                false, null, emptySpec, emptyPreview, "Project not found.");
        }

        var workingDir = TerraformIntegrity.ResolveWorkingDirectory(project, environment);
        var (outPlanFile, relativePlanFile) = ResolvePlanPaths(project, environment, planId, mode);
        var varFile = ResolveVarFile(environment, workingDir);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;

        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.Plan)
        {
            Plan = options,
            VarFile = varFile,
            OutPlanFile = outPlanFile
        };

        var request = CommandPreviewBuilder.BuildRequest(spec, exePath, workingDir);
        var chips = BuildChips(installation, request.RiskLevel, project.RequiredTerraformVersion, mode);
        var preview = CommandPreviewBuilder.BuildPreview(request, chips);
        var blockReason = DetermineBlockReason(project, environment, workingDir, installation);

        return new PlanContext(
            projectId, environmentId, planId, mode, workingDir, varFile, outPlanFile, relativePlanFile,
            environment?.IsProduction ?? false, environment?.CloudConnectionId, spec, preview, blockReason);
    }

    public async Task<PlanCreationResult> CreatePlanAsync(
        PlanContext context, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        if (context.BlockReason is not null)
            return PlanCreationResult.Blocked(context.BlockReason);

        var project = await _projects.GetAsync(context.ProjectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == context.EnvironmentId);
        if (project is null || environment is null)
            return PlanCreationResult.Blocked("Project or environment not found.");

        var installation = await _discovery.ResolveAsync(context.ProjectId, ct);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var locksDir = TerraformIntegrity.LocksDirectory(project);

        await using var envLock = await _locks.TryAcquireAsync(
            new EnvironmentLockRequest(environment.Id, locksDir, ModeOperation(context.Mode)), ct);
        if (envLock is null)
            return PlanCreationResult.Blocked($"Environment '{environment.Name}' is locked by another operation.");

        Directory.CreateDirectory(Path.GetDirectoryName(context.OutPlanFile)!);

        // 1) Run the plan (human-readable stream — Terraform masks sensitive values; safe to log).
        var planRequest = CommandPreviewBuilder.BuildRequest(context.Spec, exePath, context.WorkingDirectory);
        var planRun = await _coordinator.RunAsync(planRequest, output, captureLog: true, ct);

        if (planRun.Process.Cancelled)
            return new PlanCreationResult(null, null, null, planRun.Process, planRun.RunId, planRun.LogPath, null);
        if (planRun.Process.ExitCode != 0)
            return new PlanCreationResult(null, null, null, planRun.Process, planRun.RunId, planRun.LogPath,
                $"Terraform plan failed (exit {planRun.Process.ExitCode}). See the output for details.");

        // 2) Convert to JSON for review (raw JSON is sensitive → parsed in memory, never logged).
        var showSpec = new TerraformRunSpec(context.ProjectId, context.EnvironmentId, TerraformCommandKind.Show)
        {
            PlanFilePath = context.OutPlanFile
        };
        var showRequest = CommandPreviewBuilder.BuildRequest(showSpec, exePath, context.WorkingDirectory);
        var showRun = await _coordinator.RunAsync(showRequest, output: null, captureLog: false, ct);
        var review = PlanJsonParser.Parse(showRun.StandardOutput);

        // 3) Integrity hashes.
        var configHash = await TerraformIntegrity.ComputeConfigHashAsync(project.RootPath, context.WorkingDirectory, ct);
        var lockHash = await TerraformIntegrity.ComputeLockHashAsync(context.WorkingDirectory, ct);
        var planFileHash = File.Exists(context.OutPlanFile)
            ? await FileHashing.Sha256HexAsync(context.OutPlanFile, ct)
            : null;

        // 4) Persist the redacted safety record.
        var savedPlan = new SavedPlan
        {
            Id = context.PlanId,
            ProjectId = context.ProjectId,
            EnvironmentId = context.EnvironmentId,
            EnvironmentName = environment.Name,
            Mode = context.Mode,
            PlanCommandRunId = planRun.RunId,
            PlanFilePath = context.OutPlanFile,
            RelativePlanFilePath = context.RelativePlanFile,
            WorkingDirectory = context.WorkingDirectory,
            TerraformVersion = installation?.Version?.ToString() ?? review.TerraformVersion,
            ConfigHash = configHash,
            LockHash = lockHash,
            PlanFileHash = planFileHash,
            AddCount = review.Summary.Add,
            ChangeCount = review.Summary.Change,
            DestroyCount = review.Summary.Destroy,
            ReplaceCount = review.Summary.Replace,
            IsProductionTarget = environment.IsProduction,
            CloudConnectionId = environment.CloudConnectionId
        };
        await _plans.AddAsync(savedPlan, ct);

        // 5) Version-control the plan file in Fenrix history (git tracks it too; it is not gitignored).
        await CaptureInHistoryAsync(context.ProjectId, context.RelativePlanFile, context.OutPlanFile, FileChangeKind.Created, ct);

        _logger.LogInformation("Created {Mode} plan {PlanId} for {Project}/{Env} (+{Add} ~{Change} -{Destroy} ±{Replace})",
            context.Mode, savedPlan.Id, project.Name, environment.Name,
            savedPlan.AddCount, savedPlan.ChangeCount, savedPlan.DestroyCount, savedPlan.ReplaceCount);

        return new PlanCreationResult(savedPlan.Id, MapSummary(savedPlan), review, planRun.Process, planRun.RunId, planRun.LogPath, null);
    }

    public async Task<PlanReview> GetReviewAsync(Guid savedPlanId, CancellationToken ct = default)
    {
        var plan = await _plans.GetAsync(savedPlanId, ct);
        if (plan is null || !File.Exists(plan.PlanFilePath))
            return PlanReview.Empty;

        var installation = await _discovery.ResolveAsync(plan.ProjectId, ct);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var showSpec = new TerraformRunSpec(plan.ProjectId, plan.EnvironmentId, TerraformCommandKind.Show)
        {
            PlanFilePath = plan.PlanFilePath
        };
        var showRequest = CommandPreviewBuilder.BuildRequest(showSpec, exePath, plan.WorkingDirectory);
        var showRun = await _coordinator.RunAsync(showRequest, output: null, captureLog: false, ct);
        return PlanJsonParser.Parse(showRun.StandardOutput);
    }

    public Task<IReadOnlyList<SavedPlanSummary>> GetRecentAsync(
        Guid projectId, Guid? environmentId = null, int limit = 25, CancellationToken ct = default) =>
        _plans.GetRecentAsync(projectId, environmentId, limit, ct);

    // ---- helpers ----

    private static PlanMode ResolveMode(PlanOptions o) =>
        o.Destroy ? PlanMode.Destroy : o.RefreshOnly ? PlanMode.RefreshOnly : PlanMode.Normal;

    private static string ModeOperation(PlanMode mode) => mode switch
    {
        PlanMode.Destroy => "destroy",
        PlanMode.RefreshOnly => "refresh",
        _ => "plan"
    };

    private static (string OutPlanFile, string RelativePlanFile) ResolvePlanPaths(
        InfrastructureProject? project, ProjectEnvironment? environment, Guid planId, PlanMode mode)
    {
        if (project is null)
            return (string.Empty, string.Empty);

        var slug = ProjectScaffolder.Slug(environment?.Name ?? "env");
        var tag = mode switch { PlanMode.Destroy => "destroy", PlanMode.RefreshOnly => "refresh", _ => "plan" };
        var fileName = $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{tag}-{planId.ToString("N")[..8]}.tfplan";

        var outPlanFile = Path.Combine(TerraformIntegrity.PlansDirectory(project, slug), fileName);
        var relativePlanFile = $"plans/{slug}/{fileName}";
        return (outPlanFile, relativePlanFile);
    }

    private static string? ResolveVarFile(ProjectEnvironment? environment, string workingDir)
    {
        var varFile = environment?.VariablesFile;
        if (string.IsNullOrWhiteSpace(varFile) || string.IsNullOrWhiteSpace(workingDir))
            return null;
        var full = Path.IsPathRooted(varFile) ? varFile : Path.Combine(workingDir, varFile);
        return File.Exists(full) ? varFile : null;
    }

    private static string? DetermineBlockReason(
        InfrastructureProject? project, ProjectEnvironment? environment, string workingDir, TerraformInstallation? installation)
    {
        if (project is null)
            return "Project not found.";
        if (environment is null)
            return "Select an environment to run against.";
        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
            return $"Working directory not found: {workingDir}";
        if (installation is null)
            return "No Terraform binary found. Set the executable in Settings or install Terraform on your PATH.";
        if (installation.Version is null)
            return $"Could not read the version of the Terraform binary at {installation.ExecutablePath}.";
        if (!installation.SatisfiesConstraint(project.RequiredTerraformVersion))
            return $"Terraform {installation.Version} does not satisfy this project's required version '{project.RequiredTerraformVersion}'.";
        return null;
    }

    private List<CommandContextChip> BuildChips(
        TerraformInstallation? installation, TerraformRiskLevel risk, string? requiredVersion, PlanMode mode)
    {
        var chips = new List<CommandContextChip>
        {
            new("Terraform", installation?.Version?.ToString() ?? "not found")
        };
        if (!string.IsNullOrWhiteSpace(requiredVersion))
            chips.Add(new CommandContextChip("Requires", requiredVersion));
        if (mode != PlanMode.Normal)
            chips.Add(new CommandContextChip("Mode", mode == PlanMode.Destroy ? "destroy" : "refresh-only"));
        chips.Add(new CommandContextChip("Risk", RiskLabel(risk)));
        return chips;
    }

    private static string RiskLabel(TerraformRiskLevel risk) => risk switch
    {
        TerraformRiskLevel.ReadOnly => "read-only",
        TerraformRiskLevel.Safe => "safe",
        TerraformRiskLevel.StateChanging => "state-changing",
        TerraformRiskLevel.Destructive => "destructive",
        _ => risk.ToString()
    };

    private async Task CaptureInHistoryAsync(Guid projectId, string relativePath, string fullPath, FileChangeKind kind, CancellationToken ct)
    {
        try
        {
            await _fileHistory.RecordAsync(new FileChange
            {
                ProjectId = projectId,
                RelativePath = relativePath,
                FullPath = fullPath,
                ChangeKind = kind,
                Origin = ChangeOrigin.FenrixEditor
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not capture {Path} in file history.", relativePath);
        }
    }

    private static SavedPlanSummary MapSummary(SavedPlan p) => new(
        p.Id, p.ProjectId, p.EnvironmentId, p.EnvironmentName, p.Mode,
        p.PlanFilePath, p.RelativePlanFilePath, p.TerraformVersion,
        p.AddCount, p.ChangeCount, p.DestroyCount, p.ReplaceCount,
        p.IsProductionTarget, p.Applied, p.CreatedAt, p.AppliedAt,
        p.IsInvalidated, p.InvalidatedReason);
}
