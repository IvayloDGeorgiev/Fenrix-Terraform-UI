namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// A named, parameterised HCL scaffold shared across a team (the reusable templates deferred from
/// Phase 10). Instantiation is pure — parameters are substituted and the result is emitted through
/// the Phase 10 HCL toolkit and written via the atomic-write + file-history path. Authors config
/// only, never state. See docs/29-enterprise.md, docs/07-visual-builder.md.
/// </summary>
public sealed class ConfigTemplate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Category { get; set; }

    /// <summary>The HCL body with <c>{{param}}</c> placeholders. Canonicalised by <c>fmt</c> after write.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Default relative target file (e.g. <c>main.tf</c>); the user can override at apply time.</summary>
    public string? DefaultTargetFile { get; set; }

    public List<TemplateParameter> Parameters { get; set; } = [];

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>A single substitutable input of a <see cref="ConfigTemplate"/>.</summary>
public sealed class TemplateParameter
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid TemplateId { get; init; }

    /// <summary>Placeholder name — matches <c>{{name}}</c> in the body.</summary>
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TemplateParameterType Type { get; set; } = TemplateParameterType.String;
    public string? DefaultValue { get; set; }
    public bool Required { get; set; } = true;
    public int DisplayOrder { get; set; }
}
