using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Installs Terraform application-wide by downloading the official HashiCorp release (Phase 12). One shared copy
/// lives under <c>&lt;dataRoot&gt;\Tools\terraform\terraform.exe</c> and the <c>terraform.executable</c> setting
/// is set at Global scope, so every project uses it. Verifies the published SHA-256 before trusting the binary.
/// See docs/05-terraform-engine.md.
/// </summary>
public sealed class TerraformInstaller(
    IHttpClientFactory httpClientFactory,
    IWorkspacePaths paths,
    ISettingsService settings,
    ITerraformDiscovery discovery,
    ILogger<TerraformInstaller> logger) : ITerraformInstaller
{
    private const string CheckpointUrl = "https://checkpoint-api.hashicorp.com/v1/check/terraform";
    private const string ReleasesBase = "https://releases.hashicorp.com/terraform";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ISettingsService _settings = settings;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly ILogger<TerraformInstaller> _logger = logger;

    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<TerraformInstallResult> InstallLatestAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
            return TerraformInstallResult.Fail("Automatic install is only available on Windows.");

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(TerraformInstaller));
            client.Timeout = TimeSpan.FromMinutes(5);
            if (!client.DefaultRequestHeaders.Contains("User-Agent"))
                client.DefaultRequestHeaders.Add("User-Agent", "Fenrix-IaCStudio");

            progress?.Report("Checking the latest Terraform version…");
            var version = await GetLatestVersionAsync(client, cancellationToken).ConfigureAwait(false);
            if (version is null)
                return TerraformInstallResult.Fail("Could not determine the latest Terraform version (network issue?).");

            var platform = ResolvePlatform();
            if (platform is null)
                return TerraformInstallResult.Fail($"Unsupported architecture: {RuntimeInformation.OSArchitecture}.");

            var zipName = $"terraform_{version}_{platform}.zip";
            var zipUrl = $"{ReleasesBase}/{version}/{zipName}";
            var sumsUrl = $"{ReleasesBase}/{version}/terraform_{version}_SHA256SUMS";

            progress?.Report($"Downloading Terraform {version} ({platform})…");
            var zipBytes = await client.GetByteArrayAsync(zipUrl, cancellationToken).ConfigureAwait(false);

            progress?.Report("Verifying checksum…");
            var expected = await GetExpectedHashAsync(client, sumsUrl, zipName, cancellationToken).ConfigureAwait(false);
            if (expected is not null)
            {
                var actual = Convert.ToHexString(SHA256.HashData(zipBytes)).ToLowerInvariant();
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    return TerraformInstallResult.Fail("Checksum mismatch — the download may be corrupt. Nothing was installed.");
            }
            else
            {
                _logger.LogWarning("Could not fetch SHA256SUMS for Terraform {Version}; proceeding without checksum verification.", version);
            }

            progress?.Report("Installing…");
            var targetDir = Path.Combine(_paths.ToolsDirectory, "terraform");
            Directory.CreateDirectory(targetDir);
            var targetPath = Path.Combine(targetDir, "terraform.exe");
            ExtractExecutable(zipBytes, targetPath);

            // Point every project at this one binary (Global scope).
            await _settings.SetAsync(FenrixSettingKeys.TerraformExecutable, targetPath, SettingScope.Global, null, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report("Verifying the installed binary…");
            var installed = await _discovery.ProbeAsync(targetPath, TerraformExecutableSource.Configured, cancellationToken).ConfigureAwait(false);
            var resolvedVersion = installed?.Version?.ToString() ?? version;

            _logger.LogInformation("Installed Terraform {Version} to {Path}", resolvedVersion, targetPath);
            progress?.Report($"Terraform {resolvedVersion} installed.");
            return TerraformInstallResult.Ok(resolvedVersion, targetPath);
        }
        catch (OperationCanceledException)
        {
            return TerraformInstallResult.Fail("Install cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Terraform auto-install failed.");
            return TerraformInstallResult.Fail(ex.Message);
        }
    }

    private static async Task<string?> GetLatestVersionAsync(HttpClient client, CancellationToken ct)
    {
        try
        {
            var json = await client.GetStringAsync(CheckpointUrl, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("current_version", out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
        }
        catch (Exception)
        {
            // fall through to null
        }
        return null;
    }

    /// <summary>Fetches the SHA256SUMS file and returns the hash for the given zip filename, or null if unavailable.</summary>
    private static async Task<string?> GetExpectedHashAsync(HttpClient client, string sumsUrl, string zipName, CancellationToken ct)
    {
        try
        {
            var sums = await client.GetStringAsync(sumsUrl, ct).ConfigureAwait(false);
            foreach (var line in sums.Split('\n'))
            {
                // Each line is "<hash>  <filename>".
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && string.Equals(parts[1], zipName, StringComparison.OrdinalIgnoreCase))
                    return parts[0].Trim().ToLowerInvariant();
            }
        }
        catch (Exception)
        {
            // treat as unavailable
        }
        return null;
    }

    private static void ExtractExecutable(byte[] zipBytes, string targetPath)
    {
        using var ms = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
        var entry = archive.GetEntry("terraform.exe")
                    ?? archive.Entries.FirstOrDefault(e => e.Name.Equals("terraform.exe", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("The Terraform archive did not contain terraform.exe.");

        using var source = entry.Open();
        using var dest = File.Create(targetPath);
        source.CopyTo(dest);
    }

    private static string? ResolvePlatform() => RuntimeInformation.OSArchitecture switch
    {
        Architecture.X64 => "windows_amd64",
        Architecture.X86 => "windows_386",
        Architecture.Arm64 => "windows_arm64",
        _ => null,
    };
}
