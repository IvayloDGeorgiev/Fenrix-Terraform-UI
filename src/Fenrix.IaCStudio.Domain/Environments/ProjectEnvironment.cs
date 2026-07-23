namespace Fenrix.IaCStudio.Domain.Environments;

/// <summary>
/// An environment within a project (e.g. Dev, UAT, Live). The cloud connection is
/// bound here — never on the project — because deploy/update/manage runs per
/// environment. See docs/26-connections.md and docs/03-domain-model.md.
/// </summary>
public sealed class ProjectEnvironment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }

    public string Name { get; set; } = string.Empty;

    /// <summary>The actual directory Terraform runs in for this environment.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    public string? TerraformWorkspace { get; set; }
    public string? VariablesFile { get; set; }
    public string? BackendConfigFile { get; set; }

    /// <summary>The cloud account this environment authenticates to. Bound per environment.</summary>
    public Guid? CloudConnectionId { get; set; }

    public string? GitBranchPolicy { get; set; }

    public bool IsProduction { get; set; }
    public int DisplayOrder { get; set; }
}
