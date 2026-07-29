using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions;
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
/// Captures and caches provider schemas for the visual builder. Runs <c>terraform providers schema -json</c>
/// (read-only, no lock, not gated on a cloud connection — schemas are local, secret-free descriptions of
/// attribute shapes) through the shared coordinator with <c>captureLog:false</c> (the JSON is large; we keep
/// logs lean and parse in memory), then persists the raw JSON + a small metadata sidecar under
/// <c>Cache/terraform-schemas</c>. Later builder sessions read the cache instead of re-invoking Terraform.
/// See docs/07-visual-builder.md.
/// </summary>
public sealed class ProviderSchemaService(
    IProjectService projects,
    ITerraformDiscovery discovery,
    TerraformProcessCoordinator coordinator,
    IWorkspacePaths paths,
    ILogger<ProviderSchemaService> logger) : IProviderSchemaService
{
    private const string DefaultExecutable = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<ProviderSchemaService> _logger = logger;

    public async Task<InspectionContext> PreviewAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.ProvidersSchema);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir);
        var chips = new List<CommandContextChip>
        {
            new("Terraform", resolved.Installation?.Version?.ToString() ?? "not found"),
            new("Risk", "read-only")
        };
        var preview = CommandPreviewBuilder.BuildPreview(request, chips);
        return new InspectionContext(projectId, environmentId, TerraformCommandKind.ProvidersSchema,
            resolved.WorkingDir, spec, preview, resolved.BlockReason);
    }

    public async Task<SchemaRefreshResult> RefreshAsync(
        Guid projectId, Guid environmentId, IProgress<ProcessOutputEvent>? output = null, CancellationToken ct = default)
    {
        var resolved = await ResolveAsync(projectId, environmentId, ct);
        if (resolved.BlockReason is not null)
            return SchemaRefreshResult.Blocked(resolved.BlockReason);

        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.ProvidersSchema);
        var request = CommandPreviewBuilder.BuildRequest(spec, resolved.ExePath, resolved.WorkingDir);

        // captureLog:false — the schema JSON is large and provider-defined (no secret values), so we keep it
        // out of the run logs and parse it in memory before writing it to the offline cache.
        var run = await _coordinator.RunAsync(request, output, captureLog: false, ct);
        if (run.Process.ExitCode != 0)
        {
            // Most commonly: providers not installed yet. The streamed output already tells the user to init.
            _logger.LogInformation("providers schema exited {Code} for {Project}/{Env}.", run.Process.ExitCode, projectId, environmentId);
            return new SchemaRefreshResult(ProviderSchemaSet.Empty, null, false);
        }

        var set = ProviderSchemaJsonParser.Parse(run.StandardOutput);
        await WriteCacheAsync(projectId, environmentId, resolved.WorkingDir, run.StandardOutput, set.Providers.Count, ct);
        return new SchemaRefreshResult(set, null, true);
    }

    public async Task<ProviderSchemaSet> GetCachedAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var schemaPath = SchemaPath(projectId, environmentId);
        if (!File.Exists(schemaPath))
            return ProviderSchemaSet.Empty;
        try
        {
            var json = await File.ReadAllTextAsync(schemaPath, ct);
            return ProviderSchemaJsonParser.Parse(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read cached schema for {Project}/{Env}.", projectId, environmentId);
            return ProviderSchemaSet.Empty;
        }
    }

    public async Task<ProviderSchemaCacheInfo> GetCacheInfoAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var metaPath = MetaPath(projectId, environmentId);
        if (!File.Exists(metaPath))
            return ProviderSchemaCacheInfo.Missing;
        try
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            var meta = JsonSerializer.Deserialize<CacheMeta>(json);
            if (meta is null)
                return ProviderSchemaCacheInfo.Missing;
            return new ProviderSchemaCacheInfo(true, meta.CapturedAt, meta.ProviderCount, meta.LockHash);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ProviderSchemaCacheInfo.Missing;
        }
    }

    private async Task WriteCacheAsync(
        Guid projectId, Guid environmentId, string workingDir, string rawJson, int providerCount, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            await File.WriteAllTextAsync(SchemaPath(projectId, environmentId), rawJson, ct);

            var lockHash = await TerraformIntegrity.ComputeLockHashAsync(workingDir, ct);
            var meta = new CacheMeta(DateTimeOffset.Now, providerCount, lockHash);
            await File.WriteAllTextAsync(MetaPath(projectId, environmentId), JsonSerializer.Serialize(meta), ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not persist provider schema cache for {Project}/{Env}.", projectId, environmentId);
        }
    }

    private string CacheDirectory => Path.Combine(_paths.CacheDirectory, "terraform-schemas");
    private string SchemaPath(Guid projectId, Guid environmentId) =>
        Path.Combine(CacheDirectory, $"{projectId:N}_{environmentId:N}.json");
    private string MetaPath(Guid projectId, Guid environmentId) =>
        Path.Combine(CacheDirectory, $"{projectId:N}_{environmentId:N}.meta.json");

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

    private static string? DetermineBlockReason(
        InfrastructureProject? project, ProjectEnvironment? environment, string workingDir, TerraformInstallation? installation)
    {
        if (project is null)
            return "Project not found.";
        if (environment is null)
            return "Select an environment to capture schemas for.";
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

    private sealed record CacheMeta(DateTimeOffset CapturedAt, int ProviderCount, string? LockHash);

    private readonly record struct ResolvedContext(
        InfrastructureProject? Project,
        ProjectEnvironment? Environment,
        TerraformInstallation? Installation,
        string WorkingDir,
        string ExePath,
        string? BlockReason);
}
