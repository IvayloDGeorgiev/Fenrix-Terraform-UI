namespace Fenrix.IaCStudio.Contracts.Checks;

/// <summary>
/// A single resource's estimated monthly cost, parsed from Infracost's JSON (Phase 13 · cost). For a diff run
/// this carries the delta as well as the projected total. Amounts are decimals in the report's currency.
/// See docs/34-checks.md.
/// </summary>
/// <param name="Name">The resource address, e.g. <c>aws_instance.web</c>.</param>
/// <param name="ResourceType">The Terraform resource type, e.g. <c>aws_instance</c>.</param>
/// <param name="MonthlyCost">Projected monthly cost for this resource (the "after" figure for a diff).</param>
/// <param name="MonthlyDelta">Change in monthly cost for a diff run; null for a plain breakdown.</param>
public sealed record CostResource(
    string Name,
    string? ResourceType,
    decimal? MonthlyCost,
    decimal? MonthlyDelta);

/// <summary>
/// The estimated cost result for an environment: the projected monthly total, an optional monthly delta (when
/// run as a diff against a saved baseline), the per-resource breakdown, and any resources Infracost could not
/// price. See docs/34-checks.md.
/// </summary>
/// <param name="Available">True when the Infracost binary was resolved.</param>
/// <param name="Ran">True when Infracost actually executed.</param>
/// <param name="IsDiff">True when this was a diff against a baseline (delta figures populated).</param>
/// <param name="Currency">ISO currency code reported by Infracost (e.g. <c>USD</c>).</param>
/// <param name="TotalMonthlyCost">Projected total monthly cost.</param>
/// <param name="TotalMonthlyDelta">Change in total monthly cost for a diff run; null for a plain breakdown.</param>
/// <param name="Resources">Per-resource costs, largest first.</param>
/// <param name="UnsupportedResourceCount">Count of resources Infracost could not estimate.</param>
/// <param name="Cancelled">True when the run was cancelled.</param>
/// <param name="Error">A human-readable failure reason (e.g. missing API key). Never a secret.</param>
/// <param name="NeedsApiKey">True when the run failed specifically because no Infracost API key is configured.</param>
public sealed record CostEstimate(
    bool Available,
    bool Ran,
    bool IsDiff,
    string? Currency,
    decimal? TotalMonthlyCost,
    decimal? TotalMonthlyDelta,
    IReadOnlyList<CostResource> Resources,
    int UnsupportedResourceCount,
    bool Cancelled,
    string? Error,
    bool NeedsApiKey)
{
    public static CostEstimate NotAvailable() =>
        new(false, false, false, null, null, null, [], 0, false, null, false);

    public static CostEstimate MissingApiKey() =>
        new(true, false, false, null, null, null, [], 0, false,
            "An Infracost API key is required. Add a free key to estimate costs.", true);
}
