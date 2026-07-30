using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>A template as listed in the gallery.</summary>
public sealed record TemplateSummary(
    Guid Id, string Name, string? Description, string? Category, int ParameterCount, DateTimeOffset UpdatedAt);

/// <summary>A parameter of a template (for the apply form and editor).</summary>
public sealed record TemplateParameterModel(
    string Name, string? Description, TemplateParameterType Type, string? DefaultValue, bool Required, int DisplayOrder);

/// <summary>A template with its body and parameters (for editing / applying).</summary>
public sealed record TemplateDetail(
    Guid Id,
    string Name,
    string? Description,
    string? Category,
    string Body,
    string? DefaultTargetFile,
    IReadOnlyList<TemplateParameterModel> Parameters,
    DateTimeOffset UpdatedAt);

/// <summary>Create/update a template and its parameters.</summary>
public sealed record SaveTemplateRequest(
    Guid? Id,
    string Name,
    string? Description,
    string? Category,
    string Body,
    string? DefaultTargetFile,
    IReadOnlyList<TemplateParameterModel> Parameters);

/// <summary>Apply a template into a project's config file.</summary>
public sealed record ApplyTemplateRequest(
    Guid TemplateId,
    Guid ProjectId,
    string TargetRelativePath,
    IReadOnlyDictionary<string, string?> Values);

/// <summary>Result of applying (or previewing) a template.</summary>
public sealed record ApplyTemplateResult(
    bool Succeeded,
    string Hcl,
    IReadOnlyList<string> MissingRequired,
    IReadOnlyList<string> UnknownPlaceholders,
    string? TargetRelativePath,
    string? Error)
{
    public static ApplyTemplateResult Fail(string error) => new(false, string.Empty, [], [], null, error);
}
