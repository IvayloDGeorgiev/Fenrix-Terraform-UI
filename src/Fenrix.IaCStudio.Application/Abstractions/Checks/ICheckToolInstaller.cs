using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Abstractions.Checks;

/// <summary>
/// Installs a check tool once for the whole application when none is found — mirroring <c>ITerraformInstaller</c>.
/// Downloads the official release for the current OS/architecture, verifies its published checksum when
/// available, places the binary under the shared Fenrix Tools directory, and sets
/// <c>checks.&lt;tool&gt;.executable</c> at Global scope so every project resolves it immediately. No PATH
/// changes, no admin rights. See docs/34-checks.md.
/// </summary>
public interface ICheckToolInstaller
{
    /// <summary>True when auto-install is available for the given tool on this platform.</summary>
    bool CanInstall(CheckTool tool);

    /// <summary>
    /// Downloads and installs the latest stable release of the tool application-wide, reporting progress.
    /// Best-effort: returns a failed result with a message rather than throwing.
    /// </summary>
    Task<CheckToolInstallResult> InstallLatestAsync(
        CheckTool tool, IProgress<string>? progress = null, CancellationToken ct = default);
}
