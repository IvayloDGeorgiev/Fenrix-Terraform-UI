using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// The built-in project-template catalog (Phase 12). Each template is a complete, cost-aware Terraform starter
/// grounded in real-world best practice rather than the most expensive vendor default — e.g. no NAT gateway
/// where public subnets + strict security groups suffice, Graviton/ARM sizes, Fargate Spot, and
/// scale-to-zero / free-tier services for demo and dev work. Definitions are split across partial files by
/// provider. See docs/32-project-templates.md.
/// </summary>
internal static partial class BuiltInTemplates
{
    public static readonly IReadOnlyList<ProjectTemplate> All = BuildAll();

    private static IReadOnlyList<ProjectTemplate> BuildAll()
    {
        var list = new List<ProjectTemplate>();
        AddAws(list);
        AddAzure(list);
        AddGcp(list);
        AddDocker(list);
        AddMisc(list);
        return list;
    }

    static partial void AddAws(List<ProjectTemplate> list);
    static partial void AddAzure(List<ProjectTemplate> list);
    static partial void AddGcp(List<ProjectTemplate> list);
    static partial void AddDocker(List<ProjectTemplate> list);
    static partial void AddMisc(List<ProjectTemplate> list);

    // ── authoring helpers ──────────────────────────────────────────────────
    internal static ProjectTemplateInfo Info(
        string id, string name, string description,
        TemplateProvider provider, TemplateCategory category, TemplateCostTier tier,
        string costSummary, string[] tags, string? teardownHint = null)
        => new(id, name, description, provider, category, tier, costSummary, tags, IsBuiltIn: true)
        { TeardownHint = teardownHint };

    internal static ProjectTemplateFile F(string relativePath, string content)
        => new(relativePath, content.TrimStart('\n'));

    internal static ProjectTemplate T(ProjectTemplateInfo info, params ProjectTemplateFile[] files)
        => new(info, files);
}
