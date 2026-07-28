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
    public PlanOptions Plan { get; init; } = new();

    /// <summary>
    /// Var-file passed to <c>plan</c> (<c>-var-file</c>). Typically the environment's tfvars, resolved
    /// relative to the working directory. Never passed to <c>apply</c> — a saved plan already fixes its
    /// variables, and Terraform rejects <c>-var-file</c> with a saved plan.
    /// </summary>
    public string? VarFile { get; init; }

    /// <summary>The <c>-out</c> target when planning: the absolute path the saved plan is written to.</summary>
    public string? OutPlanFile { get; init; }

    /// <summary>The saved plan file to <c>apply</c> or <c>show</c> (absolute path). Required for those kinds.</summary>
    public string? PlanFilePath { get; init; }

    // ---- Phase 9: state & inspection options. Only the field(s) matching Kind are used. ----

    /// <summary>State move (<see cref="TerraformCommandKind.StateMove"/>) source/destination addresses.</summary>
    public StateMoveOptions StateMove { get; init; } = new();

    /// <summary>State remove (<see cref="TerraformCommandKind.StateRemove"/>) target addresses.</summary>
    public StateRemoveOptions StateRemove { get; init; } = new();

    /// <summary>Force-unlock (<see cref="TerraformCommandKind.ForceUnlock"/>) lock id.</summary>
    public ForceUnlockOptions ForceUnlock { get; init; } = new();

    /// <summary>Workspace verbs (<see cref="TerraformCommandKind.WorkspaceSelect"/>/<c>New</c>/<c>Delete</c>) target name.</summary>
    public WorkspaceOptions Workspace { get; init; } = new();

    /// <summary>Import (<see cref="TerraformCommandKind.Import"/>) / config-generation options.</summary>
    public ImportOptions Import { get; init; } = new();

    /// <summary>
    /// Optional single output name for <see cref="TerraformCommandKind.Output"/> (<c>output -json NAME</c>).
    /// Null enumerates all outputs.
    /// </summary>
    public string? OutputName { get; init; }

    /// <summary>
    /// The <c>-generate-config-out</c> target for <see cref="TerraformCommandKind.PlanGenerateConfig"/>: the
    /// absolute path Terraform writes generated HCL to for import blocks present in config.
    /// </summary>
    public string? GenerateConfigOutFile { get; init; }

    /// <summary>Absolute path to the state file for <see cref="TerraformCommandKind.StatePush"/> (the source).</summary>
    public string? StateFilePath { get; init; }
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
