using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Installs Terraform once for the whole application when none is found (Phase 12). Downloads the official
/// HashiCorp release for the current OS/architecture, verifies its published checksum, places
/// <c>terraform.exe</c> under the shared Fenrix Tools directory (in the data root, not any single project), and
/// sets the <c>terraform.executable</c> setting at <b>Global</b> scope so <em>every</em> project resolves it
/// immediately — no PATH changes, no admin rights, no separate installer. See docs/05-terraform-engine.md,
/// docs/14-settings.md.
/// </summary>
public interface ITerraformInstaller
{
    /// <summary>True when auto-install is available on this platform (Windows).</summary>
    bool IsSupported { get; }

    /// <summary>
    /// Downloads and installs the latest stable Terraform application-wide, reporting human-readable progress.
    /// Best-effort: returns a failed result with a message rather than throwing.
    /// </summary>
    Task<TerraformInstallResult> InstallLatestAsync(
        IProgress<string>? progress = null, CancellationToken cancellationToken = default);
}
