namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>
/// The optional, non-secret project manifest persisted to <c>.fenrix/project-manifest.json</c>.
/// Records the logical structure so a project re-opens consistently. Must NEVER contain
/// passwords, tokens, client secrets, or cloud access keys. See docs/03-domain-model.md.
/// </summary>
public sealed class ProjectManifest
{
    public int SchemaVersion { get; set; } = 1;
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? TerraformVersion { get; set; }
    public List<ManifestEnvironment> Environments { get; set; } = [];
}

/// <summary>An environment entry inside <see cref="ProjectManifest"/>.</summary>
public sealed class ManifestEnvironment
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Project-relative working directory for this environment (forward slashes).</summary>
    public string Path { get; set; } = string.Empty;

    public string? VariablesFile { get; set; }
    public string? BackendConfigFile { get; set; }
    public bool IsProduction { get; set; }
}
