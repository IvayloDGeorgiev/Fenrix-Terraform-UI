using Fenrix.IaCStudio.Application.Abstractions.Cloud;
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
/// The guided import assistant. CLI import (<c>terraform import ADDRESS ID</c>) writes an existing object into
/// state directly — state-changing, so confirmed + locked + blocked-when-unbound + history-recorded. Config
/// generation (Terraform 1.5+) writes an <c>import{}</c> block to a Fenrix-managed file and runs
/// <c>plan -generate-config-out=&lt;file&gt;</c> to scaffold HCL; it changes no state, so it is not gated by a typed
/// confirmation, but still authenticates (needs a bound connection) and version-controls the written files via
/// file history. See docs/22-terraform-files-model.md, docs/06-plan-apply-safety.md.
/// </summary>
public sealed class TerraformImportService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    IEnvironmentLockService locks,
    IFileHistoryStore fileHistory,
    ICloudEnvironmentComposer cloud,
    ILogger<TerraformImportService> logger) : ITerraformImportService
{
    private const string DefaultExecutable = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly IEnvironmentLockService _locks = locks;
    private readonly IFileHistoryStore _fileHistory = fileHistory;
    private readonly ICloudEnvironmentComposer _cloud = cloud;
    private readonly ILogger<TerraformImportService> _logger = logger;

    public async Task<StateOpContext> PrepareAsync(
        Guid projectId, Guid environmentId, ImportOptions options, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        var kind = options.GenerateConfigOut ? TerraformCommandKind.PlanGenerateConfig : TerraformCommandKind.Import;
        var label = options.GenerateConfigOut ? "generate config from import" : "import into state";
        var confirmPhrase = resolved.Environment?.Name ?? "confirm";

        var inputError = ValidateInputs(options);
        var spec = BuildSpec(projectId, environmentId, options, resolved);
        var blockReason = inputError ?? DetermineBlockReason(resolved);

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
            preview = new CommandPreview("terraform", "terraform", [kind == TerraformCommandKind.Import ? "import" : "plan"],
                resolved.WorkingDir, [], "terraform");
        }

        return new StateOpContext(
            projectId, environmentId, kind, label, resolved.WorkingDir,
            resolved.Environment?.CloudConnectionId, spec, preview, confirmPhrase, blockReason);
    }

    public async Task<ImportResult> ExecuteAsync(
        StateOpContext context, ApplyConfirmation confirmation, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        var prepared = await PrepareAsync(context.ProjectId, context.EnvironmentId, context.Spec.Import, ct);
        if (!prepared.CanRun)
            return ImportResult.Blocked(prepared.BlockReason ?? "Import is blocked by a safety check.");

        var resolved = await ResolveAsync(context.ProjectId, context.EnvironmentId, ct);
        if (resolved.Project is null || resolved.Environment is null)
            return ImportResult.Blocked("Project or environment not found.");

        return context.Kind == TerraformCommandKind.PlanGenerateConfig
            ? await GenerateConfigAsync(context, resolved, output, ct)
            : await CliImportAsync(context, prepared, confirmation, resolved, output, ct);
    }

    // ---- CLI import (state-changing) ----

    private async Task<ImportResult> CliImportAsync(
        StateOpContext context, StateOpContext prepared, ApplyConfirmation confirmation,
        ResolvedContext resolved, IProgress<ProcessOutputEvent>? output, CancellationToken ct)
    {
        if (!string.Equals(confirmation.TypedValue?.Trim(), prepared.ConfirmationPhrase, StringComparison.Ordinal))
            return ImportResult.Blocked($"Type '{prepared.ConfirmationPhrase}' to confirm this import.");

        var locksDir = TerraformIntegrity.LocksDirectory(resolved.Project!);
        await using var envLock = await _locks.TryAcquireAsync(
            new EnvironmentLockRequest(resolved.Environment!.Id, locksDir, "import"), ct);
        if (envLock is null)
            return ImportResult.Blocked($"Environment '{resolved.Environment.Name}' is locked by another operation.");

        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(context.Spec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
        var run = await _coordinator.RunAsync(request, output, captureLog: true, ct);

        _logger.LogInformation("Import {Address} -> state for {Project}/{Env}: {Status}",
            context.Spec.Import.Address, resolved.Project!.Name, resolved.Environment.Name,
            run.Process.Succeeded ? "succeeded" : run.Process.Cancelled ? "cancelled" : "failed");

        return new ImportResult(run.RunId, run.Process, false, null, null, null);
    }

    // ---- Config generation (Terraform 1.5+ import{} + -generate-config-out) ----

    private async Task<ImportResult> GenerateConfigAsync(
        StateOpContext context, ResolvedContext resolved, IProgress<ProcessOutputEvent>? output, CancellationToken ct)
    {
        var options = context.Spec.Import;
        var slug = Slug(options.Address!);
        var importBlockPath = Path.Combine(resolved.WorkingDir, $"fenrix-import-{slug}.tf");
        var generatedPath = context.Spec.GenerateConfigOutFile
            ?? Path.Combine(resolved.WorkingDir, $"fenrix-generated-{slug}.tf");

        // 1) Write the import block (real config on disk, source of truth) and capture it in file history.
        var importBlock = $"# Generated by Fenrix import assistant. Review and remove after config is generated.\n" +
                          $"import {{\n  to = {options.Address}\n  id = \"{EscapeHcl(options.Id!)}\"\n}}\n";
        try
        {
            await File.WriteAllTextAsync(importBlockPath, importBlock, ct);
        }
        catch (Exception ex)
        {
            return ImportResult.Blocked($"Could not write the import block to {importBlockPath}: {ex.Message}");
        }
        await CaptureInHistoryAsync(resolved.Project!, importBlockPath, FileChangeKind.Created, ct);

        // -generate-config-out fails if the target already exists; start clean.
        if (File.Exists(generatedPath))
        {
            try { File.Delete(generatedPath); } catch { /* best effort */ }
        }

        // 2) Run the generating plan under the environment lock (it refreshes state in memory only).
        var locksDir = TerraformIntegrity.LocksDirectory(resolved.Project!);
        await using var envLock = await _locks.TryAcquireAsync(
            new EnvironmentLockRequest(resolved.Environment!.Id, locksDir, "generate-config"), ct);
        if (envLock is null)
            return ImportResult.Blocked($"Environment '{resolved.Environment.Name}' is locked by another operation.");

        var genSpec = context.Spec with { GenerateConfigOutFile = generatedPath };
        var cloudEnv = await _cloud.ComposeAsync(resolved.Environment.CloudConnectionId, ct);
        var request = CommandPreviewBuilder.BuildRequest(genSpec, resolved.ExePath, resolved.WorkingDir, cloudEnv.EnvironmentVariables);
        var run = await _coordinator.RunAsync(request, output, captureLog: true, ct);

        string? generated = null;
        if (File.Exists(generatedPath))
        {
            try { generated = await File.ReadAllTextAsync(generatedPath, ct); } catch { /* ignore */ }
            await CaptureInHistoryAsync(resolved.Project!, generatedPath, FileChangeKind.Created, ct);
        }

        _logger.LogInformation("Generate-config import for {Address} in {Project}/{Env}: {Status} (generated: {Generated})",
            options.Address, resolved.Project!.Name, resolved.Environment.Name,
            run.Process.Succeeded ? "succeeded" : "failed", generated is not null);

        return new ImportResult(run.RunId, run.Process, true, generated is not null ? generatedPath : null, generated, null);
    }

    // ---- validation & context ----

    private static string? ValidateInputs(ImportOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Address))
            return "Enter the target resource address (e.g. aws_instance.web).";
        if (string.IsNullOrWhiteSpace(o.Id))
            return "Enter the real-world id of the existing object.";
        return null;
    }

    private TerraformRunSpec BuildSpec(Guid projectId, Guid environmentId, ImportOptions options, ResolvedContext resolved)
    {
        var kind = options.GenerateConfigOut ? TerraformCommandKind.PlanGenerateConfig : TerraformCommandKind.Import;
        var spec = new TerraformRunSpec(projectId, environmentId, kind)
        {
            Import = options,
            VarFile = resolved.VarFile
        };
        if (options.GenerateConfigOut && !string.IsNullOrWhiteSpace(options.Address))
            spec = spec with { GenerateConfigOutFile = Path.Combine(resolved.WorkingDir, $"fenrix-generated-{Slug(options.Address)}.tf") };
        return spec;
    }

    private string? DetermineBlockReason(ResolvedContext r)
    {
        if (r.Project is null)
            return "Project not found.";
        if (r.Environment is null)
            return "Select an environment.";
        if (r.Environment.CloudConnectionId is null)
            return "This environment has no cloud connection. Bind one before importing (authentication required).";
        if (string.IsNullOrWhiteSpace(r.WorkingDir) || !Directory.Exists(r.WorkingDir))
            return $"Working directory not found: {r.WorkingDir}";
        if (r.Installation is null)
            return "No Terraform binary found. Set the executable in Settings or install Terraform on your PATH.";
        if (r.Installation.Version is null)
            return $"Could not read the version of the Terraform binary at {r.Installation.ExecutablePath}.";
        if (!r.Installation.SatisfiesConstraint(r.Project.RequiredTerraformVersion))
            return $"Terraform {r.Installation.Version} does not satisfy this project's required version '{r.Project.RequiredTerraformVersion}'.";
        return null;
    }

    private async Task<ResolvedContext> ResolveAsync(Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == environmentId);
        var installation = await _discovery.ResolveAsync(projectId, ct);
        var workingDir = project is null ? string.Empty : TerraformIntegrity.ResolveWorkingDirectory(project, environment);
        var exePath = installation?.ExecutablePath ?? DefaultExecutable;
        var varFile = ResolveVarFile(environment, workingDir);
        return new ResolvedContext(project, environment, installation, workingDir, exePath, varFile);
    }

    private static string? ResolveVarFile(ProjectEnvironment? environment, string workingDir)
    {
        var varFile = environment?.VariablesFile;
        if (string.IsNullOrWhiteSpace(varFile) || string.IsNullOrWhiteSpace(workingDir))
            return null;
        var full = Path.IsPathRooted(varFile) ? varFile : Path.Combine(workingDir, varFile);
        return File.Exists(full) ? varFile : null;
    }

    private async Task CaptureInHistoryAsync(InfrastructureProject project, string fullPath, FileChangeKind kind, CancellationToken ct)
    {
        try
        {
            var rel = FileTrackingPolicy.ToRelative(project.RootPath, fullPath);
            await _fileHistory.RecordAsync(new FileChange
            {
                ProjectId = project.Id,
                RelativePath = rel,
                FullPath = fullPath,
                ChangeKind = kind,
                Origin = ChangeOrigin.FenrixEditor
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not capture {Path} in file history.", fullPath);
        }
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
        chips.Add(new CommandContextChip("Risk", risk == TerraformRiskLevel.Safe ? "safe" : "state-changing"));
        return chips;
    }

    private static string Slug(string address)
    {
        var chars = address.Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-').ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--", StringComparison.Ordinal))
            slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "resource" : slug;
    }

    private static string EscapeHcl(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private readonly record struct ResolvedContext(
        InfrastructureProject? Project,
        ProjectEnvironment? Environment,
        TerraformInstallation? Installation,
        string WorkingDir,
        string ExePath,
        string? VarFile);
}
