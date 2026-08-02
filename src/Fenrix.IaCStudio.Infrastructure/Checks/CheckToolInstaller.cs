using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Checks;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Checks;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Installs a check tool application-wide by downloading its official GitHub release for the current OS/arch,
/// verifying the published checksum when one is available, placing the binary under
/// <c>&lt;dataRoot&gt;\Tools\&lt;tool&gt;\</c>, and setting <c>checks.&lt;tool&gt;.executable</c> at Global scope —
/// mirroring <c>TerraformInstaller</c>. Best-effort: returns a failed result rather than throwing. No admin
/// rights and no PATH changes. See docs/34-checks.md.
/// </summary>
public sealed class CheckToolInstaller(
    IHttpClientFactory httpClientFactory,
    IWorkspacePaths paths,
    ISettingsService settings,
    CheckProcessRunner runner,
    ILogger<CheckToolInstaller> logger) : ICheckToolInstaller
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ISettingsService _settings = settings;
    private readonly CheckProcessRunner _runner = runner;
    private readonly ILogger<CheckToolInstaller> _logger = logger;

    public bool CanInstall(CheckTool tool) => OperatingSystem.IsWindows();

    public async Task<CheckToolInstallResult> InstallLatestAsync(
        CheckTool tool, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        if (!CanInstall(tool))
            return CheckToolInstallResult.Fail(tool, "Automatic install is only available on Windows.");

        var meta = CheckToolMetadata.For(tool);
        var display = meta.DisplayName;

        var archTokens = ResolveArchTokens();
        if (archTokens is null)
            return CheckToolInstallResult.Fail(tool, $"Unsupported architecture: {RuntimeInformation.OSArchitecture}.");

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(CheckToolInstaller));
            client.Timeout = TimeSpan.FromMinutes(5);
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "Fenrix-IaCStudio");
            if (!client.DefaultRequestHeaders.Contains("Accept"))
                client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");

            progress?.Report($"Finding the latest {display} release…");
            var release = await GetLatestReleaseAsync(client, meta.GitHubOwner, meta.GitHubRepo, ct).ConfigureAwait(false);
            if (release is null)
                return CheckToolInstallResult.Fail(tool, $"Could not query the latest {display} release (network issue?).");

            var (version, assets) = release.Value;

            var asset = SelectAsset(assets, meta.AssetOs, archTokens);
            if (asset is null)
                return CheckToolInstallResult.Fail(tool,
                    $"No matching {display} download was found for windows/{RuntimeInformation.OSArchitecture}.");

            progress?.Report($"Downloading {display} {version}…");
            var bytes = await client.GetByteArrayAsync(asset.Url, ct).ConfigureAwait(false);

            progress?.Report("Verifying checksum…");
            var expected = await GetExpectedHashAsync(client, assets, asset.Name, ct).ConfigureAwait(false);
            if (expected is not null)
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    return CheckToolInstallResult.Fail(tool, "Checksum mismatch — the download may be corrupt. Nothing was installed.");
            }
            else
            {
                _logger.LogWarning("Could not fetch a checksum for {Tool} {Version}; proceeding without verification.", tool, version);
            }

            progress?.Report("Installing…");
            var targetDir = Path.Combine(_paths.ToolsDirectory, meta.BaseExecutableName);
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, meta.BaseExecutableName + ".exe");
            ExtractBinary(asset.Name, bytes, meta.BaseExecutableName, targetPath);

            await _settings.SetAsync(meta.SettingKey, targetPath, SettingScope.Global, null, ct).ConfigureAwait(false);

            progress?.Report("Verifying the installed binary…");
            var resolvedVersion = await ProbeVersionAsync(tool, targetPath, ct).ConfigureAwait(false) ?? version;

            _logger.LogInformation("Installed {Tool} {Version} to {Path}", tool, resolvedVersion, targetPath);
            progress?.Report($"{display} {resolvedVersion} installed.");
            return CheckToolInstallResult.Ok(tool, resolvedVersion, targetPath);
        }
        catch (OperationCanceledException)
        {
            return CheckToolInstallResult.Fail(tool, "Install cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tool} auto-install failed.", tool);
            return CheckToolInstallResult.Fail(tool, ex.Message);
        }
    }

    private sealed record Asset(string Name, string Url);

    private static async Task<(string Version, IReadOnlyList<Asset> Assets)?> GetLatestReleaseAsync(
        HttpClient client, string owner, string repo, CancellationToken ct)
    {
        try
        {
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            var json = await client.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag)) return null;

            var assets = new List<Asset>();
            if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in arr.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                    var dl = a.TryGetProperty("browser_download_url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dl))
                        assets.Add(new Asset(name!, dl!));
                }
            }

            return (tag!.TrimStart('v'), assets);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Picks the release binary asset for windows + the current arch, preferring zip, then exe, then tar.gz.</summary>
    private static Asset? SelectAsset(IReadOnlyList<Asset> assets, string osToken, IReadOnlyList<string> archTokens)
    {
        static bool IsBinaryExt(string n) =>
            n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);

        static bool IsSidecar(string n) =>
            n.Contains("checksum", StringComparison.OrdinalIgnoreCase)
            || n.Contains("sha256", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".sig", StringComparison.OrdinalIgnoreCase)
            || n.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            || n.Contains("sbom", StringComparison.OrdinalIgnoreCase);

        var candidates = assets.Where(a =>
                a.Name.Contains(osToken, StringComparison.OrdinalIgnoreCase)
                && archTokens.Any(tok => a.Name.Contains(tok, StringComparison.OrdinalIgnoreCase))
                && IsBinaryExt(a.Name)
                && !IsSidecar(a.Name))
            .ToList();

        int Rank(Asset a) =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? 0
            : a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? 1 : 2;

        return candidates.OrderBy(Rank).FirstOrDefault();
    }

    /// <summary>Best-effort: a per-asset <c>.sha256</c> file, else a generic checksums file listing the asset.</summary>
    private static async Task<string?> GetExpectedHashAsync(
        HttpClient client, IReadOnlyList<Asset> assets, string assetName, CancellationToken ct)
    {
        try
        {
            var perAsset = assets.FirstOrDefault(a =>
                a.Name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase));
            if (perAsset is not null)
            {
                var content = await client.GetStringAsync(perAsset.Url, ct).ConfigureAwait(false);
                var token = content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (IsHex(token)) return token!.ToLowerInvariant();
            }

            var checksums = assets.FirstOrDefault(a =>
                a.Name.Contains("checksum", StringComparison.OrdinalIgnoreCase)
                || a.Name.Contains("SHA256SUMS", StringComparison.OrdinalIgnoreCase));
            if (checksums is not null)
            {
                var content = await client.GetStringAsync(checksums.Url, ct).ConfigureAwait(false);
                foreach (var line in content.Split('\n'))
                {
                    if (!line.Contains(assetName, StringComparison.OrdinalIgnoreCase)) continue;
                    var token = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (IsHex(token)) return token!.ToLowerInvariant();
                }
            }
        }
        catch (Exception)
        {
            // treat as unavailable
        }
        return null;
    }

    private static bool IsHex(string? s) =>
        !string.IsNullOrWhiteSpace(s) && s.Length == 64 && s.All(Uri.IsHexDigit);

    private static void ExtractBinary(string assetName, byte[] bytes, string baseName, string targetPath)
    {
        if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(targetPath, bytes);
            return;
        }

        if (assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(bytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals(baseName + ".exe", StringComparison.OrdinalIgnoreCase))
                        ?? archive.Entries.FirstOrDefault(e => e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        ?? throw new InvalidOperationException($"The {baseName} archive did not contain an executable.");
            using var source = entry.Open();
            using var dest = File.Create(targetPath);
            source.CopyTo(dest);
            return;
        }

        if (assetName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || assetName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            using var ms = new MemoryStream(bytes);
            using var gz = new GZipStream(ms, CompressionMode.Decompress);
            using var tar = new TarReader(gz);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                var name = Path.GetFileName(entry.Name);
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || name.Equals(baseName, StringComparison.OrdinalIgnoreCase))
                {
                    using var dest = File.Create(targetPath);
                    entry.DataStream?.CopyTo(dest);
                    return;
                }
            }
            throw new InvalidOperationException($"The {baseName} archive did not contain an executable.");
        }

        throw new InvalidOperationException($"Unsupported archive format for {assetName}.");
    }

    private async Task<string?> ProbeVersionAsync(CheckTool tool, string path, CancellationToken ct)
    {
        try
        {
            var workingDir = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var exec = await _runner.ExecuteAsync(
                path, workingDir, CheckToolMetadata.VersionArguments(tool),
                new Dictionary<string, string>(0), "version", ct).ConfigureAwait(false);
            foreach (var block in new[] { exec.StandardOutput, exec.StandardError })
                foreach (var line in (block ?? "").Split('\n'))
                {
                    var t = line.Trim();
                    if (t.Length > 0) return t;
                }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not probe installed {Tool} version.", tool);
        }
        return null;
    }

    private static IReadOnlyList<string>? ResolveArchTokens() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => ["amd64", "x86_64", "64bit", "64-bit"],
        Architecture.Arm64 => ["arm64", "aarch64"],
        Architecture.X86 => ["386", "x86", "32bit", "32-bit"],
        _ => null,
    };
}
