namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>Whether a resource is Terraform-managed or a read-only data source.</summary>
public enum ResourceMode
{
    Managed = 0,
    Data = 1
}

/// <summary>
/// The net action for a resource, derived from Terraform's <c>change.actions</c> array
/// (<c>["create"]</c>, <c>["update"]</c>, <c>["delete"]</c>, <c>["delete","create"]</c> → replace, …).
/// See docs/06-plan-apply-safety.md.
/// </summary>
public enum ChangeAction
{
    NoOp = 0,
    Create = 1,
    Read = 2,
    Update = 3,
    Delete = 4,
    Replace = 5,

    /// <summary>Removed from state without destroying the real object (<c>["forget"]</c>, TF 1.7+).</summary>
    Forget = 6
}

/// <summary>
/// One attribute's before/after values for the comparison pane. Values are already redacted: sensitive
/// attributes are reduced to a placeholder and values not yet known are marked
/// <see cref="UnknownAfter"/>. See docs/06-plan-apply-safety.md and docs/11-secrets.md.
/// </summary>
public sealed record AttributeChange(
    string Name,
    string? Before,
    string? After,
    bool Sensitive,
    bool UnknownAfter)
{
    /// <summary>True when the rendered before and after differ (a genuine change to show).</summary>
    public bool Changed => !string.Equals(Before, After, StringComparison.Ordinal) || UnknownAfter;
}

/// <summary>
/// A single planned resource change, redacted for display. <see cref="Attributes"/> is the flattened
/// top-level before/after set. See docs/06-plan-apply-safety.md.
/// </summary>
public sealed record PlanResourceChange(
    string Address,
    string? ModuleAddress,
    ResourceMode Mode,
    string Type,
    string Name,
    string ProviderName,
    ChangeAction Action,
    string? ActionReason,
    IReadOnlyList<string> ReplacePaths,
    IReadOnlyList<AttributeChange> Attributes)
{
    public bool IsReplace => Action == ChangeAction.Replace;
    public bool IsDestroy => Action is ChangeAction.Delete;
    public bool IsCreate => Action == ChangeAction.Create;
    public bool IsUpdate => Action == ChangeAction.Update;
}

/// <summary>A planned change to a root output value (redacted). See docs/06-plan-apply-safety.md.</summary>
public sealed record PlanOutputChange(string Name, ChangeAction Action, bool Sensitive, bool UnknownAfter);

/// <summary>
/// Counts backing the Add / Change / Destroy / Replace summary cards. Replacements are counted on their
/// own line (not folded into add+destroy) so the review can surface them distinctly.
/// See docs/06-plan-apply-safety.md.
/// </summary>
public sealed record PlanChangeSummary(int Add, int Change, int Destroy, int Replace, int Read, int NoOp, int Drift)
{
    /// <summary>Resource operations that actually change something (add+change+destroy+replace).</summary>
    public int Total => Add + Change + Destroy + Replace;

    public bool HasDestructive => Destroy > 0 || Replace > 0;
    public bool HasChanges => Total > 0;

    public static readonly PlanChangeSummary Empty = new(0, 0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The fully parsed, redacted result of <c>terraform show -json &lt;plan&gt;</c>: resource changes,
/// detected drift, output changes, and the summary counts. Parsed in memory and never persisted raw —
/// only redacted summaries reach the database. See docs/06-plan-apply-safety.md and docs/11-secrets.md.
/// </summary>
public sealed record PlanReview(
    string? FormatVersion,
    string? TerraformVersion,
    IReadOnlyList<PlanResourceChange> ResourceChanges,
    IReadOnlyList<PlanResourceChange> DriftChanges,
    IReadOnlyList<PlanOutputChange> OutputChanges,
    PlanChangeSummary Summary)
{
    public bool IsEmpty => ResourceChanges.Count == 0 && OutputChanges.Count == 0 && DriftChanges.Count == 0;

    public static readonly PlanReview Empty =
        new(null, null, [], [], [], PlanChangeSummary.Empty);
}
