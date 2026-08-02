using Fenrix.IaCStudio.Application.Abstractions.Checks;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Checks;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Resolves the check-tool binaries, mirroring <c>TerraformDiscovery</c>: the configured
/// <c>checks.&lt;tool&gt;.executable</c> setting (project scope first) wins, then the system <c>PATH</c>. Reads
/// each tool's version via <c>--version</c> through the shared safe runner. See docs/34-checks.md.
/// </summary>
public sealed class CheckToolDiscovery(
    ISettingsService settings,
    CheckProcessRunner runner,
    ICheckToolInstaller installer,
    ILogger<CheckToolDiscovery> logger) : ICheckToolDiscovery
{
    private readonly ISettingsService _settings = settings;
    private readonly CheckProcessRunner _runner = runner;
    private readonly ICheckToolInstaller _installer = installer;
    private readonly ILogger<CheckToolDiscovery> _logger = logger;

    public async Task<CheckToolStatus> ResolveAsync(CheckTool tool, Guid? projectId = null, CancellationToken ct = default)
    {
        var canInstall = _installer.CanInstall(tool);

        var configured = await _settings.GetAsync(CheckToolMetadata.SettingKey(tool), projectId, null, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return await ProbeAsync(tool, configured!, CheckToolSource.Configured, canInstall, ct).ConfigureAwait(false);

        var onPath = FindOnPath(tool);
        if (onPath is not null)
            return await ProbeAsync(tool, onPath, CheckToolSource.Path, canInstall, ct).ConfigureAwait(false);

        return CheckToolStatus.Missing(tool, canInstall);
    }

    public async Task<IReadOnlyList<CheckToolStatus>> ResolveAllAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var tools = new[] { CheckTool.TfLint, CheckTool.Tfsec, CheckTool.Trivy, CheckTool.Infracost };
        var results = new List<CheckToolStatus>(tools.Length);
        foreach (var tool in tools)
            results.Add(await ResolveAsync(tool, projectId, ct).ConfigureAwait(false));
        return results;
    }

    private async Task<CheckToolStatus> ProbeAsync(
        CheckTool tool, string path, CheckToolSource source, bool canInstall, CancellationToken ct)
    {
        var workingDir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
        string? version = null;
        try
        {
            var exec = await _runner.ExecuteAsync(
                path, workingDir, CheckToolMetadata.VersionArguments(tool),
                new Dictionary<string, string>(0), $"{CheckToolMetadata.DisplayName(tool)} --version", ct)
                .ConfigureAwait(false);

            if (!exec.Process.Cancelled)
                version = FirstMeaningfulLine(exec.StandardOutput, exec.StandardError);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not probe {Tool} at {Path}", tool, path);
        }

        return new CheckToolStatus(tool, true, path, version, source, canInstall);
    }

    private static string? FirstMeaningfulLine(string stdout, string stderr)
    {
        foreach (var block in new[] { stdout, stderr })
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            foreach (var line in block.Split('\n'))
            {
                var t = line.Trim();
                if (t.Length > 0) return t;
            }
        }
        return null;
    }

    private static string? FindOnPath(CheckTool tool)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar)) return null;

        var names = CheckToolMetadata.ExecutableNames(tool);
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in names)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch { continue; }
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }
}
