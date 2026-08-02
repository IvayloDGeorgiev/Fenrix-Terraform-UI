using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Checks;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Application.Checks;
using Fenrix.IaCStudio.Contracts.Checks;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Security;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Estimates cloud cost for an environment with Infracost (Phase 13 · cost). Runs <c>infracost breakdown</c>
/// (projected monthly cost + per-resource) and <c>infracost diff</c> against a saved baseline (the plan delta).
/// The free API key lives only in the secret store; it is injected as <c>INFRACOST_API_KEY</c> at run time and
/// never placed in args, history, or logs. Standalone and read-only. See docs/34-checks.md.
/// </summary>
public sealed class CostEstimationService(
    IProjectService projects,
    ICheckToolDiscovery discovery,
    CheckProcessRunner runner,
    ISecretStore secrets,
    IWorkspacePaths paths,
    ILogger<CostEstimationService> logger) : ICostEstimationService
{
    // The Infracost API key lives under this Credential Manager target; only a reference is used in code.
    private static readonly SecretReference ApiKeyRef = new()
    {
        Provider = SecretProvider.WindowsCredentialManager,
        ReferenceKey = "Fenrix:checks:infracost",
        DisplayName = "Infracost API key"
    };

    private readonly IProjectService _projects = projects;
    private readonly ICheckToolDiscovery _discovery = discovery;
    private readonly CheckProcessRunner _runner = runner;
    private readonly ISecretStore _secrets = secrets;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<CostEstimationService> _logger = logger;

    public async Task<bool> HasApiKeyAsync(CancellationToken ct = default)
    {
        if (!_secrets.IsSupported(ApiKeyRef.Provider)) return false;
        var value = await _secrets.RetrieveAsync(ApiKeyRef, ct).ConfigureAwait(false);
        return !string.IsNullOrWhiteSpace(value);
    }

    public Task SetApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API key cannot be empty.", nameof(apiKey));
        return _secrets.StoreAsync(ApiKeyRef, apiKey.Trim(), ct);
    }

    public Task ClearApiKeyAsync(CancellationToken ct = default) => _secrets.DeleteAsync(ApiKeyRef, ct);

    public Task<CostEstimate> EstimateAsync(
        Guid projectId, Guid environmentId, IProgress<string>? progress = null, CancellationToken ct = default)
        => RunAsync(projectId, environmentId, Mode.Breakdown, progress, ct);

    public Task<CostEstimate> DiffAsync(
        Guid projectId, Guid environmentId, IProgress<string>? progress = null, CancellationToken ct = default)
        => RunAsync(projectId, environmentId, Mode.Diff, progress, ct);

    public Task<CostEstimate> SaveBaselineAsync(
        Guid projectId, Guid environmentId, IProgress<string>? progress = null, CancellationToken ct = default)
        => RunAsync(projectId, environmentId, Mode.SaveBaseline, progress, ct);

    private enum Mode { Breakdown, Diff, SaveBaseline }

    private async Task<CostEstimate> RunAsync(
        Guid projectId, Guid environmentId, Mode mode, IProgress<string>? progress, CancellationToken ct)
    {
        var infracost = await _discovery.ResolveAsync(CheckTool.Infracost, projectId, ct).ConfigureAwait(false);
        if (!infracost.Installed || infracost.ExecutablePath is null)
            return CostEstimate.NotAvailable();

        var apiKey = _secrets.IsSupported(ApiKeyRef.Provider)
            ? await _secrets.RetrieveAsync(ApiKeyRef, ct).ConfigureAwait(false)
            : null;
        if (string.IsNullOrWhiteSpace(apiKey))
            return CostEstimate.MissingApiKey();

        var project = await _projects.GetAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project not found.");
        var env = project.Environments.FirstOrDefault(e => e.Id == environmentId)
            ?? throw new InvalidOperationException("Environment not found.");

        var workingDir = ResolveWorkingDir(project.RootPath, env.WorkingDirectory);
        if (!Directory.Exists(workingDir))
            return new CostEstimate(true, false, false, null, null, null, [], 0, false,
                "The environment's working directory does not exist.", false);

        var baselinePath = BaselinePath(projectId, environmentId);

        // The API key travels only as a process-scoped env var — never in args, history, or logs.
        var envVars = new Dictionary<string, string>
        {
            ["INFRACOST_API_KEY"] = apiKey!,
            ["INFRACOST_SKIP_UPDATE_CHECK"] = "true",
            ["NO_COLOR"] = "1"
        };

        bool asDiff = mode == Mode.Diff;
        List<string> args;
        switch (mode)
        {
            case Mode.Diff:
                if (!File.Exists(baselinePath))
                {
                    // No baseline yet — fall back to a breakdown and hint the user to save one.
                    var fallback = await RunBreakdownAsync(infracost.ExecutablePath, workingDir, envVars, null, progress, ct).ConfigureAwait(false);
                    return fallback with { Error = fallback.Error ?? "No saved baseline yet — showing the current breakdown. Save a baseline to see deltas." };
                }
                progress?.Report("Running Infracost diff…");
                args = ["diff", "--path", ".", "--compare-to", baselinePath, "--format", "json"];
                break;

            case Mode.SaveBaseline:
                progress?.Report("Saving cost baseline…");
                Directory.CreateDirectory(Path.GetDirectoryName(baselinePath)!);
                var saved = await RunBreakdownAsync(infracost.ExecutablePath, workingDir, envVars, baselinePath, progress, ct).ConfigureAwait(false);
                return saved;

            default:
                progress?.Report("Running Infracost breakdown…");
                return await RunBreakdownAsync(infracost.ExecutablePath, workingDir, envVars, null, progress, ct).ConfigureAwait(false);
        }

        // Diff path.
        var exec = await _runner.ExecuteAsync(infracost.ExecutablePath, workingDir, args, envVars, "Infracost diff", ct).ConfigureAwait(false);
        return Interpret(exec, asDiff: true);
    }

    private async Task<CostEstimate> RunBreakdownAsync(
        string exe, string workingDir, IReadOnlyDictionary<string, string> envVars, string? outFile,
        IProgress<string>? progress, CancellationToken ct)
    {
        List<string> args = ["breakdown", "--path", ".", "--format", "json"];
        if (outFile is not null)
        {
            args.Add("--out-file");
            args.Add(outFile);
        }

        var exec = await _runner.ExecuteAsync(exe, workingDir, args, envVars, "Infracost breakdown", ct).ConfigureAwait(false);

        // When writing to a file, stdout is a log rather than JSON — read the file back to parse.
        if (outFile is not null && File.Exists(outFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(outFile, ct).ConfigureAwait(false);
                return InfracostJsonParser.Parse(json, asDiff: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not read the Infracost baseline file.");
            }
        }

        return Interpret(exec, asDiff: false);
    }

    private static CostEstimate Interpret(CheckProcessRunner.CheckExecution exec, bool asDiff)
    {
        if (exec.Process.Cancelled)
            return new CostEstimate(true, true, asDiff, null, null, null, [], 0, true, null, false);

        var stdout = exec.StandardOutput;
        if (!string.IsNullOrWhiteSpace(stdout) && stdout.TrimStart().StartsWith('{'))
            return InfracostJsonParser.Parse(stdout, asDiff);

        // No JSON — decide whether it's a missing/invalid key or another failure, using stderr only.
        var stderr = exec.StandardError ?? string.Empty;
        var needsKey = stderr.Contains("api key", StringComparison.OrdinalIgnoreCase)
                       || stderr.Contains("INFRACOST_API_KEY", StringComparison.OrdinalIgnoreCase)
                       || stderr.Contains("unauthor", StringComparison.OrdinalIgnoreCase)
                       || stderr.Contains("401", StringComparison.Ordinal);
        var error = FirstLine(stderr) ?? $"Infracost exited with code {exec.Process.ExitCode}.";
        return new CostEstimate(true, true, asDiff, null, null, null, [], 0, false, error, needsKey);
    }

    private string BaselinePath(Guid projectId, Guid environmentId)
        => Path.Combine(_paths.CacheDirectory, "infracost", $"{projectId:N}_{environmentId:N}.json");

    private static string? FirstLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        foreach (var line in s.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t;
        }
        return null;
    }

    private static string ResolveWorkingDir(string projectRoot, string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir)) return projectRoot;
        return Path.IsPathRooted(workingDir) ? workingDir : Path.Combine(projectRoot, workingDir);
    }
}
