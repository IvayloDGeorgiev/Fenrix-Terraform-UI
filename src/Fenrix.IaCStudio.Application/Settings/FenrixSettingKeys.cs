namespace Fenrix.IaCStudio.Application.Settings;

/// <summary>Well-known setting keys. See docs/14-settings.md.</summary>
public static class FenrixSettingKeys
{
    // Appearance
    public const string Theme = "appearance.theme";                 // light | dark | system
    public const string Density = "appearance.density";             // compact | comfortable
    public const string ReducedMotion = "appearance.reducedMotion"; // true | false

    // General
    public const string ReopenLastProject = "general.reopenLastProject";
    public const string DefaultProjectsDirectory = "general.defaultProjectsDirectory";

    // Terraform
    public const string TerraformExecutable = "terraform.executable";
    public const string TerraformDefaultParallelism = "terraform.defaultParallelism";

    // Git
    public const string GitExecutable = "git.executable";

    // Workspace
    public const string DataRootOverride = "workspace.dataRoot";
}
