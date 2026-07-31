using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Advanced, state-changing state operations (<c>state mv/rm/push</c>, <c>force-unlock</c>, workspace
/// <c>select/new/delete</c>) plus the read-only helpers <c>workspace list</c> and <c>state pull</c>. Mutations
/// are gated behind a typed confirmation (the environment name), acquire the per-environment lock, are blocked
/// when the environment has no bound cloud connection (Phase 8 authentication-required rule), and record
/// redacted history through the shared coordinator. See docs/05-terraform-engine.md, docs/06-plan-apply-safety.md.
/// </summary>
public sealed class TerraformStateService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    IEnvironmentLockService locks,
    ICloudEnvironmentComposer cloud,
    IAuthorizationService authorization,
    ILogger<TerraformStateService> logger) : ITerraformStateService
{
    private const string DefaultExecutable = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly IEnvironmentLockService _locks = locks;
    private readonly ICloudEnvironmentComposer _cloud = cloud;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly ILogger<TerraformStateService> _logger = logger;

    public async Task<StateOpContext> PrepareAsync(TerraformRunSpec spec, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(spec.ProjectId, spec.EnvironmentId, ct);
        var label = OperationLabel(spec.Kind);
        var confirmPhrase = resolved.Environment?.Name ?? "confirm";

        // Validate inputs first so the catalog never throws while building the preview.
        var inputError = ValidateInputs(spec);
        var blockReason = inputError ?? DetermineBlockReason(resolved, spec.Kind);

        CommandPreview preview;
        if (inputError is null && resolved.Project is not null)
        {
            var cloudEnv = await _cloud.ComposeAsync(resolved.Environment?.CloudConnectionId, ct);
            var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
            var chips = BuildChips(resolved.Installation, request.RiskLevel, resolved.Project.RequiredTerraformVersion, label);
            chips.Add(new CommandContextChip("Cloud", cloudEnv.HasConnection ? cloudEnv.IdentityChip! : "none — bind a connection"));
            preview = CommandPreviewBuilder.BuildPreview(request, chips);
        }
        else
        {
            preview = PlaceholderPreview(spec.Kind, resolved.WorkingDir);
        }

        return new StateOpContext(
            spec.ProjectId, spec.EnvironmentId, spec.Kind, label, resolved.WorkingDir,
            resolved.Environment?.CloudConnectionId, spec, preview, confirmPhrase, blockReason);
    }

    public async Task<StateOpResult> ExecuteAsync(
        StateOpContext context, ApplyConfirmation confirmation, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        // Re-derive a fresh context to re-validate right before running (guards against a changed environment).
        var prepared = await PrepareAsync(context.Spec, ct);
        if (!prepared.CanRun)
            return StateOpResult.Blocked(prepared.BlockReason ?? "This operation is blocked by a safety check.");

        if (!string.Equals(confirmation.TypedValue?.Trim(), prepared.ConfirmationPhrase, StringComparison.Ordinal))
            return StateOpResult.Blocked($"Type '{prepared.ConfirmationPhrase}' to confirm this {prepared.OperationLabel}.");

        // Enterprise RBAC: force-unlock needs ForceUnlock; every other state mutation needs ManageState
        // (allow-all when mode off). A denial self-audits.
        var permission = context.Kind == TerraformCommandKind.ForceUnlock ? Permission.ForceUnlock : Permission.ManageState;
        var authz = await _authorization.AuthorizeAsync(
            permission, context.ProjectId, context.EnvironmentId, target: prepared.OperationLabel, cancellationToken: ct);
        if (!authz.Allowed)
            return StateOpResult.Blocked(authz.Reason ?? "You are not permitted to run this state operation.");

        var resolved = await ResolveAsync(context.ProjectId, context.EnvironmentId, ct);
        if (resolved.Project is null || resolved.Environment is null)
            return StateOpResult.Blocked("Project or environment not found.");

        var locksDir = TerraformIntegrity.LocksDirectory(resolved.Project);
        await using var envLock = await _locks.TryAcquireAsync(
            new EnvironmentLockRequest(resolved.Environment.Id, locksDir, context.OperationLabel), ct);
        if (envLock is null)
            return StateOpResult.Blocked($"Environment '{resolved.Environment.Name}' is locked by another operation.");

        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(context.Spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
        var run = await _coordinator.RunAsync(request, output, captureLog: true, ct);

        // Persist the environment's active workspace on a successful select/new.
        if (run.Process.Succeeded &&
            context.Kind is TerraformCommandKind.WorkspaceSelect or TerraformCommandKind.WorkspaceNew &&
            !string.IsNullOrWhiteSpace(context.Spec.Workspace.Name))
        {
            await _projects.SetEnvironmentWorkspaceAsync(context.ProjectId, context.EnvironmentId, context.Spec.Workspace.Name, ct);
        }

        _logger.LogInformation("State op {Op} for {Project}/{Env}: {Status}",
            context.OperationLabel, resolved.Project.Name, resolved.Environment.Name,
            run.Process.Succeeded ? "succeeded" : run.Process.Cancelled ? "cancelled" : "failed");

        return new StateOpResult(run.RunId, run.Process, null);
    }

    public async Task<WorkspaceSnapshot> GetWorkspacesAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        if (resolved.BlockReason is not null || resolved.Project is null)
            return WorkspaceSnapshot.Empty;

        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.WorkspaceList);
        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment?.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
        var run = await _coordinator.RunAsync(request, output: null, captureLog: true, ct);
        return run.Process.ExitCode != 0 ? WorkspaceSnapshot.Empty : WorkspaceListParser.Parse(run.StandardOutput);
    }

    public async Task<StateOpResult> PullToFileAsync(
        Guid projectId, Guid environmentId, string destinationPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
            return StateOpResult.Blocked("Choose a destination file for the pulled state.");

        var resolved = await ResolveAsync(projectId, environmentId, ct);
        if (resolved.BlockReason is not null || resolved.Project is null)
            return StateOpResult.Blocked(resolved.BlockReason ?? "Project not found.");

        // state pull exposes plaintext state (secrets) → treat as a state operation and require ManageState.
        var authz = await _authorization.AuthorizeAsync(
            Permission.ManageState, projectId, environmentId, target: "state pull", cancellationToken: ct);
        if (!authz.Allowed)
            return StateOpResult.Blocked(authz.Reason ?? "You are not permitted to pull remote state.");

        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.StatePull);
        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment?.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);

        // state pull is read-only but its output can contain plaintext secrets → never logged. We write the
        // captured stdout to the chosen file (the user's own backup); no lock is taken.
        var run = await _coordinator.RunAsync(request, output: null, captureLog: false, ct);
        if (run.Process.Succeeded)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                await File.WriteAllTextAsync(destinationPath, run.StandardOutput, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not write pulled state to {Path}.", destinationPath);
                return StateOpResult.Blocked($"Pulled state, but could not write it to {destinationPath}: {ex.Message}");
            }
        }
        return new StateOpResult(run.RunId, run.Process, null);
    }

    // ---- validation & context ----

    private static string? ValidateInputs(TerraformRunSpec spec) => spec.Kind switch
    {
        TerraformCommandKind.StateMove when string.IsNullOrWhiteSpace(spec.StateMove.Source) || string.IsNullOrWhiteSpace(spec.StateMove.Destination)
            => "Enter both the source and the destination resource address.",
        TerraformCommandKind.StateRemove when spec.StateRemove.Addresses.Count == 0 || spec.StateRemove.Addresses.Any(string.IsNullOrWhiteSpace)
            => "Enter at least one resource address to remove.",
        TerraformCommandKind.StatePush when string.IsNullOrWhiteSpace(spec.StateFilePath)
            => "Choose the state file to push.",
        TerraformCommandKind.ForceUnlock when string.IsNullOrWhiteSpace(spec.ForceUnlock.LockId)
            => "Enter the lock id reported by the stuck operation.",
        TerraformCommandKind.WorkspaceSelect or TerraformCommandKind.WorkspaceNew or TerraformCommandKind.WorkspaceDelete
            when string.IsNullOrWhiteSpace(spec.Workspace.Name)
            => "Enter the workspace name.",
        _ => null
    };

    private string? DetermineBlockReason(ResolvedContext r, TerraformCommandKind kind)
    {
        if (r.Project is null)
            return "Project not found.";
        if (r.Environment is null)
            return "Select an environment.";
        // Phase 8 authentication-required rule: every state-changing operation needs a bound connection.
        if (r.Environment.CloudConnectionId is null)
            return "This environment has no cloud connection. Bind one before running state operations (authentication required).";
        if (string.IsNullOrWhiteSpace(r.WorkingDir) || !Directory.Exists(r.WorkingDir))
            return $"Working directory not found: {r.WorkingDir}";
        if (r.Installation is null)
            return "No Terraform binary found. Set the executable in Settings or install Terraform on your PATH.";
        if (r.Installation.Version is null)
            return $"Could not read the version of the Terraform binary at {r.Installation.ExecutablePath}.";
        if (!r.Installation.SatisfiesConstraint(r.Project.RequiredTerraformVersion))
            return $"Terraform {r.Installation.Version} does not satisfy this project's required version '{r.Project.RequiredTerraformVersion}'.";

        var locksDir = TerraformIntegrity.LocksDirectory(r.Project);
        var active = _locks.GetActive(r.Environment.Id, locksDir);
        if (active is not null && !active.IsStale)
            return $"Environment is locked by a {active.Operation} operation (pid {active.ProcessId}).";
        return null;
    }

    private async Task<ResolvedContext> ResolveAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == environmentId);
        var installation = await _discovery.ResolveAsync(projectId, ct);
        var workingDir = project is null ? string.Empty : TerraformIntegrity.ResolveWorkingDirectory(project, environment);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        return new ResolvedContext(project, environment, installation, workingDir, exePath, null);
    }

    private static string OperationLabel(TerraformCommandKind kind) => kind switch
    {
        TerraformCommandKind.StateMove => "state move",
        TerraformCommandKind.StateRemove => "state remove",
        TerraformCommandKind.StatePush => "state push",
        TerraformCommandKind.ForceUnlock => "force-unlock",
        TerraformCommandKind.WorkspaceSelect => "workspace select",
        TerraformCommandKind.WorkspaceNew => "workspace new",
        TerraformCommandKind.WorkspaceDelete => "workspace delete",
        _ => kind.ToString()
    };

    private static CommandPreview PlaceholderPreview(TerraformCommandKind kind, string workingDir)
    {
        var cmd = OperationLabel(kind);
        return new CommandPreview("terraform", "terraform", [cmd], workingDir, [], $"terraform {cmd}");
    }

    private static List<CommandContextChip> BuildChips(
        TerraformInstallation? installation, TerraformRiskLevel risk, string? requiredVersion, string label)
    {
        var chips = new List<CommandContextChip>
        {
            new("Terraform", installation?.Version?.ToString() ?? "not found"),
            new("Operation", label)
        };
        if (!string.IsNullOrWhiteSpace(requiredVersion))
            chips.Add(new CommandContextChip("Requires", requiredVersion));
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

    private readonly record struct ResolvedContext(
        InfrastructureProject? Project,
        ProjectEnvironment? Environment,
        TerraformInstallation? Installation,
        string WorkingDir,
        string ExePath,
        string? BlockReason);
}
