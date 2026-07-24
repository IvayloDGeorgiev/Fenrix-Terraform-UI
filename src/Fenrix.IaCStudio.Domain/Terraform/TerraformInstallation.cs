namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// A discovered Terraform binary: its resolved path, parsed version (when <c>terraform version</c>
/// could be read), and where it came from. See docs/05-terraform-engine.md.
/// </summary>
public sealed record TerraformInstallation(
    string ExecutablePath,
    TerraformVersion? Version,
    TerraformExecutableSource Source,
    string? Platform = null)
{
    /// <summary>True when the version could be read from the binary.</summary>
    public bool IsUsable => Version is not null;

    /// <summary>
    /// Evaluates this installation against a project's required-version constraint. A null or blank
    /// constraint is always satisfied. An unreadable version cannot be proven compliant, so it fails.
    /// </summary>
    public bool SatisfiesConstraint(string? requiredVersionExpression)
    {
        if (string.IsNullOrWhiteSpace(requiredVersionExpression))
            return true;
        if (Version is null)
            return false;
        return TerraformVersionConstraint.TryParse(requiredVersionExpression, out var constraint)
            && constraint!.IsSatisfiedBy(Version);
    }
}
