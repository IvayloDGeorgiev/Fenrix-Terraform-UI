namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>The editing shape Fenrix presents for a Terraform variable, derived from its declared type.</summary>
public enum VariableKind
{
    /// <summary><c>string</c> — a text field (rendered quoted).</summary>
    String,
    /// <summary><c>number</c> — a numeric field (raw).</summary>
    Number,
    /// <summary><c>bool</c> — a true/false toggle.</summary>
    Bool,
    /// <summary>list/map/set/object/tuple/any — a raw HCL field.</summary>
    Complex
}

/// <summary>
/// One variable merged from its declaration (<c>variable "x" { … }</c>) and the environment's tfvars value
/// (Phase 12 variables manager). The value is stored as raw HCL exactly as it appears in the tfvars file.
/// See docs/33-variables.md.
/// </summary>
/// <param name="Name">Variable name.</param>
/// <param name="TypeExpression">The declared type expression, e.g. <c>string</c>, <c>map(string)</c>.</param>
/// <param name="Kind">How Fenrix will edit it.</param>
/// <param name="Description">The declared description, if any.</param>
/// <param name="Sensitive">True when declared <c>sensitive = true</c> — the value is masked in the UI.</param>
/// <param name="HasDefault">True when the declaration provides a default.</param>
/// <param name="DefaultRaw">The default value as raw HCL, if declared.</param>
/// <param name="ValueRaw">The current value from the environment's tfvars as raw HCL, or null if unset.</param>
public sealed record ManagedVariable(
    string Name,
    string TypeExpression,
    VariableKind Kind,
    string? Description,
    bool Sensitive,
    bool HasDefault,
    string? DefaultRaw,
    string? ValueRaw)
{
    /// <summary>True when the variable has no default and therefore must be given a value.</summary>
    public bool IsRequired => !HasDefault;

    /// <summary>True when the environment's tfvars sets a value for it.</summary>
    public bool IsSet => ValueRaw is not null;

    /// <summary>True when a required variable has no value set anywhere — a plan/apply would prompt or fail.</summary>
    public bool IsMissing => IsRequired && !IsSet;
}

/// <summary>The variables view for one environment: the target tfvars file and the merged variable list.</summary>
public sealed record EnvironmentVariables(
    string TfvarsFileName,
    IReadOnlyList<ManagedVariable> Variables)
{
    public int MissingCount => Variables.Count(v => v.IsMissing);
}

/// <summary>An edit to persist: the variable name and its new raw HCL value (null/empty ⇒ unset / remove).</summary>
public sealed record VariableValueEdit(string Name, string? Raw);
