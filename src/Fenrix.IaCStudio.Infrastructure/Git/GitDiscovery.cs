using System.Text;
using System.Text.RegularExpressions;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Git;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Git;

/// <summary>
/// Discovers the Git binary and reads its version via <c>git --version</c>. Resolution prefers the
/// configured executable (Settings <c>git.executable</c>, project scope first) and falls back to the system
/// <c>PATH</c>. Mirrors <c>TerraformDiscovery</c>. See docs/08-git-engine.md.
/// </summary>
public sealed partial class GitDiscovery(
    ISettingsService settings,
    IProcessRunner runner,
    ILogger<GitDiscovery> logger) : IGitDiscovery
{
    private static readonly string[] ExecutableNames =
        OperatingSystem.IsWindows() ? ["git.exe", "git"] : ["git"];

    private readonly ISettingsService _settings = settings;
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<GitDiscovery> _logger = logger;

    [GeneratedRegex(@"git version (?<v>\d+\.\d+\.\d+(\.\w+)?)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionLine();

    public async Task<GitInstallation?> ResolveAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var configured = await _settings.GetAsync(FenrixSettingKeys.GitExecutable, projectId, null, ct);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return await ProbeAsync(configured, ct);

        var onPath = FindOnPath();
        if (onPath is not null)
            return await ProbeAsync(onPath, ct);

        _logger.LogWarning("No Git binary found (configured path unset/invalid and none on PATH).");
        return null;
    }

    private async Task<GitInstallation?> ProbeAsync(string executablePath, CancellationToken ct)
    {
        if (!File.Exists(executablePath))
            return null;

        var workingDir = Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory();
        var request = new ProcessStartRequest(
            executablePath, workingDir, ["--version"], new Dictionary<string, string>(0), "git --version");

        var buffer = new StringBuilder();
        var collector = new Progress<ProcessOutputEvent>(e =>
        {
            if (e.Stream == OutputStream.Stdout)
                buffer.AppendLine(e.Text);
        });

        try
        {
            var result = await _runner.RunAsync(request, collector, ct);
            if (result.Cancelled)
                return null;

            var version = ParseVersion(buffer.ToString());
            return new GitInstallation(executablePath, version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe Git binary at {Executable}", executablePath);
            return null;
        }
    }

    internal static string? ParseVersion(string output)
    {
        var m = VersionLine().Match(output ?? string.Empty);
        return m.Success ? m.Groups["v"].Value : null;
    }

    private static string? FindOnPath()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            return null;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in ExecutableNames)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch { continue; }
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
