using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Read-only inspection: <c>show -json</c> (current state), <c>output -json</c>, and <c>graph</c>, plus
/// refresh-only drift (delegated to the plan service). Reuses the shared coordinator with
/// <c>captureLog:false</c> for the JSON commands (their output can carry sensitive values → parsed in memory,
/// redacted, never logged), composes the bound cloud connection just-in-time, and never takes the
/// per-environment lock. See docs/25-execution-lifecycle.md "Read-only inspection", docs/05-terraform-engine.md.
/// </summary>
public sealed class TerraformInspectionService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    ICloudEnvironmentComposer cloud,
    ITerraformPlanService planService,
    ILogger<TerraformInspectionService> logger) : ITerraformInspectionService
{
    private const string DefaultExecutable = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly ICloudEnvironmentComposer _cloud = cloud;
    private readonly ITerraformPlanService _planService = planService;
    private readonly ILogger<TerraformInspectionService> _logger = logger;

    public async Task<InspectionContext> PreviewAsync(
        Guid projectId, Guid environmentId, TerraformCommandKind kind, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        var spec = new TerraformRunSpec(projectId, environmentId, kind);
        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment?.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);

        var chips = BuildChips(resolved.Installation, request.RiskLevel, resolved.Project?.RequiredTerraformVersion);
        if (cloudEnv.HasConnection)
            chips.Add(new CommandContextChip("Cloud", cloudEnv.IdentityChip!));
        var preview = CommandPreviewBuilder.BuildPreview(request, chips);

        return new InspectionContext(projectId, environmentId, kind, resolved.WorkingDir, spec, preview, resolved.BlockReason);
    }

    public async Task<StateSnapshot> GetStateAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var run = await RunReadOnlyAsync(projectId, environmentId, TerraformCommandKind.StateShow, captureLog: false, ct);
        return run is null || run.Process.ExitCode != 0 ? StateSnapshot.Empty : StateJsonParser.Parse(run.StandardOutput);
    }

    public async Task<OutputCollection> GetOutputsAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        // output -json exits non-zero when there are no outputs in some versions; parse whatever we captured.
        var run = await RunReadOnlyAsync(projectId, environmentId, TerraformCommandKind.Output, captureLog: false, ct);
        return run is null ? OutputCollection.Empty : OutputJsonParser.Parse(run.StandardOutput);
    }

    public async Task<DependencyGraph> GetGraphAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        // graph output is DOT with no sensitive values → safe to log.
        var run = await RunReadOnlyAsync(projectId, environmentId, TerraformCommandKind.Graph, captureLog: true, ct);
        return run is null || run.Process.ExitCode != 0 ? DependencyGraph.Empty : GraphDotParser.Parse(run.StandardOutput);
    }

    public Task<PlanCreationResult> CheckDriftAsync(
        Guid projectId, Guid environmentId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        RunDriftAsync(projectId, environmentId, output, ct);

    private async Task<PlanCreationResult> RunDriftAsync(
        Guid projectId, Guid environmentId, IProgress<ProcessOutputEvent>? output, CancellationToken ct)
    {
        var context = await _planService.PreparePlanAsync(projectId, environmentId, new PlanOptions { RefreshOnly = true }, ct);
        if (!context.CanRun)
            return PlanCreationResult.Blocked(context.BlockReason ?? "Drift check is blocked.");
        return await _planService.CreatePlanAsync(context, output, ct);
    }

    // ---- shared execution ----

    private async Task<TerraformProcessCoordinator.CoordinatedRun?> RunReadOnlyAsync(
        Guid projectId, Guid environmentId, TerraformCommandKind kind, bool captureLog, CancellationToken ct)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        if (resolved.BlockReason is not null)
        {
            _logger.LogInformation("Inspection {Kind} blocked for {Project}/{Env}: {Reason}", kind, projectId, environmentId, resolved.BlockReason);
            return null;
        }

        var spec = new TerraformRunSpec(projectId, environmentId, kind);
        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment?.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
        return await _coordinator.RunAsync(request, output: null, captureLog, ct);
    }

    private async Task<ResolvedContext> ResolveAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == environmentId);
        var installation = await _discovery.ResolveAsync(projectId, ct);
        var workingDir = project is null ? string.Empty : TerraformIntegrity.ResolveWorkingDirectory(project, environment);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var block = DetermineBlockReason(project, environment, workingDir, installation);
        return new ResolvedContext(project, environment, installation, workingDir, exePath, block);
    }

    /// <summary>Read-only inspection is not blocked on a missing cloud connection — only on the binary/dir/version.</summary>
    private static string? DetermineBlockReason(
        InfrastructureProject? project, ProjectEnvironment? environment, string workingDir, TerraformInstallation? installation)
    {
        if (project is null)
            return "Project not found.";
        if (environment is null)
            return "Select an environment to inspect.";
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

    private static List<CommandContextChip> BuildChips(TerraformInstallation? installation, TerraformRiskLevel risk, string? requiredVersion)
    {
        var chips = new List<CommandContextChip>
        {
            new("Terraform", installation?.Version?.ToString() ?? "not found")
        };
        if (!string.IsNullOrWhiteSpace(requiredVersion))
            chips.Add(new CommandContextChip("Requires", requiredVersion));
        chips.Add(new CommandContextChip("Risk", "read-only"));
        return chips;
    }

    private readonly record struct ResolvedContext(
        InfrastructureProject? Project,
        ProjectEnvironment? Environment,
        TerraformInstallation? Installation,
        string WorkingDir,
        string ExePath,
        string? BlockReason);
}
