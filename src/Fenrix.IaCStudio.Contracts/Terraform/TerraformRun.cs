using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// What the user wants to run: a project + environment + command, plus the per-command options. Only
/// the option bag matching <see cref="Kind"/> is used. See docs/05-terraform-engine.md.
/// </summary>
public sealed record TerraformRunSpec(Guid ProjectId, Guid EnvironmentId, TerraformCommandKind Kind)
{
    public InitOptions Init { get; init; } = new();
    public FormatOptions Format { get; init; } = new();
    public ValidateOptions Validate { get; init; } = new();
}

/// <summary>
/// A resolved, ready-to-run plan: the exact request, its redacted preview, the resolved binary, and —
/// when non-null — the reason Fenrix refuses to run (e.g. a version-constraint violation). The preview
/// and the request share one argument list, so what the user sees is exactly what executes. See
/// docs/23-command-transparency.md.
/// </summary>
public sealed record TerraformRunPlan(
    TerraformCommandRequest Request,
    CommandPreview Preview,
    TerraformInstallation? Installation,
    string? BlockReason)
{
    /// <summary>True when the plan may be executed (a binary was found and no policy blocks it).</summary>
    public bool CanRun => BlockReason is null && Installation is not null;
}

/// <summary>Outcome of executing a <see cref="TerraformRunPlan"/>. See docs/25-execution-lifecycle.md.</summary>
public sealed record TerraformRunResult(
    Guid RunId,
    ProcessResult Process,
    string? OutputLogPath,
    TerraformValidationResult? Validation = null);
