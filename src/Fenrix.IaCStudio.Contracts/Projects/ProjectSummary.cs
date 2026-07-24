namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>A lightweight projection of a registered project for lists and the recent-projects view.</summary>
public sealed class ProjectSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RootPath { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsLinked { get; set; }
    public bool IsArchived { get; set; }
    public int EnvironmentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastOpenedAt { get; set; }
}
