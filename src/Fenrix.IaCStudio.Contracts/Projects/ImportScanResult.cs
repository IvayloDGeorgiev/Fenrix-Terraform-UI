namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>
/// The result of scanning an existing folder for the Add-Existing-Project wizard.
/// Nothing on disk is moved or rewritten; this only describes what was found and
/// suggests mappings the user can correct. See docs/03-domain-model.md.
/// </summary>
public sealed class ImportScanResult
{
    public string RootPath { get; set; } = string.Empty;

    public bool IsGitRepository { get; set; }
    public string? RepositoryRootPath { get; set; }

    public string? DetectedTerraformVersion { get; set; }
    public bool HasBackendConfiguration { get; set; }

    public int TerraformFileCount { get; set; }
    public List<string> DetectedProviders { get; set; } = [];

    /// <summary>Directories that look like environments, with suggested mappings.</summary>
    public List<EnvironmentMapping> SuggestedEnvironments { get; set; } = [];

    /// <summary>True when no Terraform files were found at all (import still allowed, with a warning).</summary>
    public bool LooksEmpty => TerraformFileCount == 0;
}

/// <summary>A suggested or user-corrected mapping from an environment name to a directory.</summary>
public sealed class EnvironmentMapping
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Project-relative directory Terraform runs in (forward slashes).</summary>
    public string RelativePath { get; set; } = string.Empty;

    public string? VariablesFile { get; set; }
    public string? BackendConfigFile { get; set; }
    public string? TerraformWorkspace { get; set; }
    public bool IsProduction { get; set; }
    public bool Include { get; set; } = true;
}
