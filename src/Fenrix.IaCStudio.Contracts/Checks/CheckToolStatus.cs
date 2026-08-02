namespace Fenrix.IaCStudio.Contracts.Checks;

/// <summary>Where a resolved check-tool binary came from — mirrors the Terraform executable source.</summary>
public enum CheckToolSource
{
    /// <summary>Resolved from the <c>checks.&lt;tool&gt;.executable</c> setting.</summary>
    Configured = 0,
    /// <summary>Found on the system <c>PATH</c>.</summary>
    Path = 1
}

/// <summary>
/// Discovery result for a single check tool: whether a binary was found, where, its version, and how it was
/// resolved. Drives the tool-status ribbon and the "install if missing" action on the Checks screen.
/// See docs/34-checks.md.
/// </summary>
/// <param name="Tool">The tool.</param>
/// <param name="Installed">True when a working binary was resolved.</param>
/// <param name="ExecutablePath">Full path to the resolved binary, when installed.</param>
/// <param name="Version">The reported version string, when it could be read.</param>
/// <param name="Source">How the binary was resolved.</param>
/// <param name="CanAutoInstall">True when Fenrix can download and install this tool on this platform.</param>
public sealed record CheckToolStatus(
    CheckTool Tool,
    bool Installed,
    string? ExecutablePath,
    string? Version,
    CheckToolSource? Source,
    bool CanAutoInstall)
{
    public static CheckToolStatus Missing(CheckTool tool, bool canAutoInstall) =>
        new(tool, false, null, null, null, canAutoInstall);
}

/// <summary>
/// Outcome of a one-click check-tool install. Fenrix downloads the official release for the current OS/arch,
/// verifies its published checksum when one is available, drops the binary into the shared Tools directory, and
/// points the <c>checks.&lt;tool&gt;.executable</c> setting at it (Global scope). See docs/34-checks.md.
/// </summary>
/// <param name="Success">True when a working binary was installed and configured.</param>
/// <param name="Tool">Which tool was installed.</param>
/// <param name="Version">The installed version on success.</param>
/// <param name="ExecutablePath">Full path to the installed binary on success.</param>
/// <param name="Error">A human-readable reason on failure. Never a secret.</param>
public sealed record CheckToolInstallResult(
    bool Success, CheckTool Tool, string? Version, string? ExecutablePath, string? Error)
{
    public static CheckToolInstallResult Ok(CheckTool tool, string version, string path) =>
        new(true, tool, version, path, null);

    public static CheckToolInstallResult Fail(CheckTool tool, string error) =>
        new(false, tool, null, null, error);
}
