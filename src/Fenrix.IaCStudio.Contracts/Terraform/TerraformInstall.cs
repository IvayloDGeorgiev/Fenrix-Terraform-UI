namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// Outcome of a one-click Terraform install (Phase 12). Fenrix downloads the official HashiCorp Windows build,
/// verifies its published SHA-256, drops <c>terraform.exe</c> into the data root's Tools folder, and points the
/// <c>terraform.executable</c> setting at it. See docs/05-terraform-engine.md.
/// </summary>
/// <param name="Success">True when a working binary was installed and configured.</param>
/// <param name="Version">The installed version (e.g. <c>1.9.5</c>) on success.</param>
/// <param name="ExecutablePath">Full path to the installed <c>terraform.exe</c> on success.</param>
/// <param name="Error">A human-readable reason on failure, never a secret.</param>
public sealed record TerraformInstallResult(bool Success, string? Version, string? ExecutablePath, string? Error)
{
    public static TerraformInstallResult Ok(string version, string path) => new(true, version, path, null);
    public static TerraformInstallResult Fail(string error) => new(false, null, null, error);
}
