namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>The cloud a project template targets. See docs/32-project-templates.md.</summary>
public enum TemplateProvider { Aws, Azure, Gcp, Docker, MultiCloud, Other }

/// <summary>What kind of infrastructure a template stands up.</summary>
public enum TemplateCategory { StaticSite, WebApp, Serverless, Containers, VirtualMachine, Networking, Database, Kubernetes, Starter }

/// <summary>Rough running cost, used for filtering + a badge. Free = fits the provider's always-free / free trial tier or scales to zero.</summary>
public enum TemplateCostTier { Free, Low, Medium }

/// <summary>One file a template writes into an environment's working directory.</summary>
/// <param name="RelativePath">
/// Path relative to the environment working dir (e.g. <c>main.tf</c>, <c>network.tf</c>). The special name
/// <c>terraform.tfvars</c> is written as the environment's own <c>&lt;slug&gt;.tfvars</c> so it loads via the
/// environment's var-file, keeping Fenrix's per-environment values model.
/// </param>
/// <param name="Content">The file's UTF-8 text.</param>
public sealed record ProjectTemplateFile(string RelativePath, string Content);

/// <summary>Template metadata (no file bodies) — enough to list, filter, and preview in the gallery.</summary>
public sealed record ProjectTemplateInfo(
    string Id,
    string Name,
    string Description,
    TemplateProvider Provider,
    TemplateCategory Category,
    TemplateCostTier CostTier,
    string CostSummary,
    IReadOnlyList<string> Tags,
    bool IsBuiltIn)
{
    /// <summary>A one-line note on how to tear it down cheaply (surfaced for Free/demo templates).</summary>
    public string? TeardownHint { get; init; }
}

/// <summary>A full template: metadata + the files it prefills into each environment.</summary>
public sealed record ProjectTemplate(ProjectTemplateInfo Info, IReadOnlyList<ProjectTemplateFile> Files);

/// <summary>Request to create/update a user template (from the management UI or an existing project).</summary>
public sealed record SaveTemplateRequest(
    string Name,
    string Description,
    TemplateProvider Provider,
    TemplateCategory Category,
    TemplateCostTier CostTier,
    string CostSummary,
    IReadOnlyList<string> Tags,
    IReadOnlyList<ProjectTemplateFile> Files)
{
    /// <summary>Existing id when updating; null when creating.</summary>
    public string? Id { get; init; }
}
