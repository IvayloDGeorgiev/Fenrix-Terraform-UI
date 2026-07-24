namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>Severity of a pre-apply safety check. See docs/06-plan-apply-safety.md.</summary>
public enum PreflightSeverity
{
    /// <summary>Informational context; never blocks apply.</summary>
    Info = 0,

    /// <summary>The user should be aware (deletions, replacements, production, branch/uncommitted); does not block.</summary>
    Warning = 1,

    /// <summary>Apply is refused until resolved (missing/altered plan, invalidated, environment changed).</summary>
    Blocker = 2
}

/// <summary>One evaluated pre-apply check. See docs/06-plan-apply-safety.md.</summary>
public sealed record PreflightCheck(string Label, bool Passed, PreflightSeverity Severity, string? Detail = null);

/// <summary>
/// The outcome of evaluating the apply safety gates for a saved plan: the exact apply command preview, the
/// list of checks, whether a typed production confirmation is required, and whether apply may proceed.
/// See docs/06-plan-apply-safety.md and ADR-0003.
/// </summary>
public sealed record ApplyPreflight(
    Guid SavedPlanId,
    bool CanApply,
    bool RequiresTypedConfirmation,
    string? ConfirmationPhrase,
    CommandPreview? Preview,
    IReadOnlyList<PreflightCheck> Checks)
{
    public IEnumerable<PreflightCheck> Blockers =>
        Checks.Where(c => !c.Passed && c.Severity == PreflightSeverity.Blocker);

    public IEnumerable<PreflightCheck> Warnings =>
        Checks.Where(c => c.Severity == PreflightSeverity.Warning);

    public IEnumerable<PreflightCheck> Passed =>
        Checks.Where(c => c.Passed && c.Severity != PreflightSeverity.Warning);
}

/// <summary>What a single resource operation is doing during apply.</summary>
public enum ApplyResourceAction
{
    Create = 0,
    Update = 1,
    Delete = 2,
    Replace = 3,
    Read = 4,
    Unknown = 5
}

/// <summary>Lifecycle of a resource operation as apply progresses.</summary>
public enum ApplyResourceStatus
{
    Pending = 0,
    InProgress = 1,
    Complete = 2,
    Errored = 3
}

/// <summary>
/// One structured progress event parsed from a line of <c>terraform apply -json</c>. The UI folds these
/// into a per-resource live view (Creating… → Created) alongside the raw stream. Attribute values are not
/// carried here, so no sensitive data is surfaced. See docs/25-execution-lifecycle.md.
/// </summary>
public sealed record ApplyProgressEvent(
    string Address,
    string ResourceType,
    string Provider,
    ApplyResourceAction Action,
    ApplyResourceStatus Status,
    double? ElapsedSeconds,
    string? Message,
    DateTimeOffset Timestamp);

/// <summary>Outcome of applying a saved plan. See docs/25-execution-lifecycle.md.</summary>
public sealed record ApplyResult(
    Guid SavedPlanId,
    Guid RunId,
    ProcessResult Process,
    int Added,
    int Changed,
    int Destroyed,
    string? OutputLogPath);
