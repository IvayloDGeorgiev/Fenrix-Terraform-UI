using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Discovers Terraform binaries and reads their versions via <c>terraform version -json</c>. Resolution
/// prefers the configured executable (Settings <c>terraform.executable</c>, project scope first) and
/// falls back to the system <c>PATH</c>. See docs/05-terraform-engine.md.
/// </summary>
public sealed class TerraformDiscovery(
    ISettingsService settings,
    IProcessRunner runner,
    ILogger<TerraformDiscovery> logger) : ITerraformDiscovery
{
    private static readonly string[] ExecutableNames =
        OperatingSystem.IsWindows() ? ["terraform.exe", "terraform"] : ["terraform"];

    private readonly ISettingsService _settings = settings;
    private readonly IProcessRunner _runner = runner;
    private readonly ILogger<TerraformDiscovery> _logger = logger;

    public async Task<TerraformInstallation?> ResolveAsync(Guid? projectId = null, CancellationToken ct = default)
    {
        var configured = await _settings.GetAsync(FenrixSettingKeys.TerraformExecutable, projectId, null, ct);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return await ProbeAsync(configured, TerraformExecutableSource.Configured, ct);

        var onPath = FindOnPath();
        if (onPath is not null)
            return await ProbeAsync(onPath, TerraformExecutableSource.Path, ct);

        _logger.LogWarning("No Terraform binary found (configured path unset/invalid and none on PATH).");
        return null;
    }

    public async Task<TerraformInstallation?> ProbeAsync(string executablePath, TerraformExecutableSource source, CancellationToken ct = default)
    {
        if (!File.Exists(executablePath))
            return null;

        var workingDir = Path.GetDirectoryName(executablePath) ?? Directory.GetCurrentDirectory();
        var request = new TerraformCommandRequest(
            Guid.Empty, Guid.Empty, TerraformCommandKind.Version,
            executablePath, workingDir, "version", ["version", "-json"],
            new Dictionary<string, string>(0), TerraformRiskLevel.ReadOnly);

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

            var version = ParseVersionJson(buffer.ToString(), out var platform);
            if (version is null)
                _logger.LogWarning("Could not parse version output from {Executable}", executablePath);

            return new TerraformInstallation(executablePath, version, source, platform);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to probe Terraform binary at {Executable}", executablePath);
            return null;
        }
    }

    public async Task<IReadOnlyList<TerraformInstallation>> DiscoverAllAsync(CancellationToken ct = default)
    {
        var found = new List<TerraformInstallation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var configured = await _settings.GetAsync(FenrixSettingKeys.TerraformExecutable, null, null, ct);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured) && seen.Add(configured))
        {
            var probed = await ProbeAsync(configured, TerraformExecutableSource.Configured, ct);
            if (probed is not null) found.Add(probed);
        }

        foreach (var path in EnumeratePathCandidates())
        {
            if (!seen.Add(path)) continue;
            var probed = await ProbeAsync(path, TerraformExecutableSource.Path, ct);
            if (probed is not null) found.Add(probed);
        }

        return found;
    }

    /// <summary>Parses <c>terraform_version</c> and <c>platform</c> from <c>version -json</c> output.</summary>
    internal static TerraformVersion? ParseVersionJson(string json, out string? platform)
    {
        platform = null;
        var trimmed = json?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            if (root.TryGetProperty("platform", out var p) && p.ValueKind == JsonValueKind.String)
                platform = p.GetString();
            if (root.TryGetProperty("terraform_version", out var v) && v.ValueKind == JsonValueKind.String
                && TerraformVersion.TryParse(v.GetString(), out var parsed))
                return parsed;
        }
        catch (JsonException)
        {
            // Not JSON (older binary or an error banner) — caller falls back to a null version.
        }
        return null;
    }

    private static string? FindOnPath() => EnumeratePathCandidates().FirstOrDefault();

    private static IEnumerable<string> EnumeratePathCandidates()
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathVar))
            yield break;

        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var name in ExecutableNames)
            {
                string candidate;
                try { candidate = Path.Combine(dir, name); }
                catch { continue; }
                if (File.Exists(candidate))
                    yield return candidate;
            }
        }
    }
}
