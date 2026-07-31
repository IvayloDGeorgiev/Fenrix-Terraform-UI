namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>
/// Input for creating a brand-new project with the recommended structure.
/// See docs/03-domain-model.md.
/// </summary>
public sealed class CreateProjectRequest
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The directory the new project folder is created inside. If null, the Fenrix Projects directory is used.</summary>
    public string? ParentDirectory { get; set; }

    public string? Description { get; set; }
    public string? RequiredTerraformVersion { get; set; }

    /// <summary>Create a <c>.git</c> repo (init) and a <c>.gitignore</c> for the project.</summary>
    public bool InitializeGit { get; set; } = true;

    /// <summary>Write the <c>.fenrix/project-manifest.json</c> manifest.</summary>
    public bool WriteManifest { get; set; } = true;

    /// <summary>Environments to scaffold. Defaults to Dev / UAT / Live when empty.</summary>
    public List<NewEnvironmentSpec> Environments { get; set; } = [];

    /// <summary>
    /// Optional project-template id to start from (built-in or user). When set, the template's files prefill
    /// each environment's working directory after scaffolding. Null / empty ⇒ a blank starter. See
    /// docs/32-project-templates.md.
    /// </summary>
    public string? TemplateId { get; set; }

    /// <summary>The default set used when the caller supplies no environments.</summary>
    public static List<NewEnvironmentSpec> DefaultEnvironments() =>
    [
        new() { Name = "Dev",  IsProduction = false },
        new() { Name = "UAT",  IsProduction = false },
        new() { Name = "Live", IsProduction = true  }
    ];
}

/// <summary>A requested environment for a new project.</summary>
public sealed class NewEnvironmentSpec
{
    public string Name { get; set; } = string.Empty;
    public bool IsProduction { get; set; }

    /// <summary>
    /// The cloud connection this environment binds to (from the global library). Optional at creation — the
    /// wizard's "apply one to all" pre-fills each environment's own binding; unbound environments are flagged
    /// and blocked from state-changing runs until one is chosen. See docs/26-connections.md.
    /// </summary>
    public Guid? CloudConnectionId { get; set; }
}
