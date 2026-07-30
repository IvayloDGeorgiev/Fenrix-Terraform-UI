using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Enterprise;

/// <summary>An org-policy verdict for one action: whether it's blocked, whether approval is now required, and why.</summary>
public sealed record PolicyVerdict(
    bool Blocked,
    bool RequiresApproval,
    IReadOnlyList<string> Reasons)
{
    public static readonly PolicyVerdict Clear = new(false, false, []);
}

/// <summary>
/// Pure evaluation of an <see cref="OrgPolicy"/> against the facts of an action. Policy can only <em>tighten</em>
/// — it adds an approval requirement or a hard block, never a relaxation. No IO, so it is unit-testable and is
/// covered by the reference port. Consumed by the deploy flow and the apply preflight. See docs/29-enterprise.md.
/// </summary>
public static class PolicyEvaluator
{
    /// <summary>The facts a policy is evaluated against (gathered by the caller).</summary>
    public readonly record struct PolicyInputs(
        bool IsProduction,
        string EnvironmentName,
        bool IsDestroy,
        string? CurrentBranch,
        bool? RepositoryIsPrivate,
        string? TerraformVersion);

    /// <summary>Evaluates the policy. Returns <see cref="PolicyVerdict.Clear"/> when policy is null (mode off).</summary>
    public static PolicyVerdict Evaluate(OrgPolicy? policy, PolicyInputs i)
    {
        if (policy is null) return PolicyVerdict.Clear;

        var reasons = new List<string>();
        var blocked = false;
        var requiresApproval = false;

        // Approval on production, or on any named environment.
        if (i.IsProduction && policy.RequireApprovalForProduction)
        {
            requiresApproval = true;
            reasons.Add("Organisation policy requires approval for production deployments.");
        }
        if (policy.RequireApprovalForEnvironments.Any(
                n => string.Equals(n, i.EnvironmentName, StringComparison.OrdinalIgnoreCase)))
        {
            requiresApproval = true;
            reasons.Add($"Organisation policy requires approval for the '{i.EnvironmentName}' environment.");
        }

        // Hard block: production destroy.
        if (i.IsDestroy && i.IsProduction && policy.BlockProductionDestroy)
        {
            blocked = true;
            reasons.Add("Organisation policy forbids destroying production infrastructure.");
        }

        // Hard block: production must be on the required branch.
        if (i.IsProduction && !string.IsNullOrWhiteSpace(policy.RequiredBranchForProduction))
        {
            var ok = i.CurrentBranch is not null &&
                     string.Equals(i.CurrentBranch, policy.RequiredBranchForProduction, StringComparison.Ordinal);
            if (!ok)
            {
                blocked = true;
                reasons.Add(
                    $"Organisation policy requires production to deploy from branch '{policy.RequiredBranchForProduction}' " +
                    $"(currently on '{i.CurrentBranch ?? "?"}').");
            }
        }

        // Advisory-turned-block: a public repository when private is required.
        if (policy.RequirePrivateRepositories && i.RepositoryIsPrivate == false)
        {
            blocked = true;
            reasons.Add("Organisation policy requires a private repository (plans/state may carry secrets).");
        }

        return new PolicyVerdict(blocked, requiresApproval, reasons);
    }

    /// <summary>
    /// Checks the resolved Terraform version against the org's allow constraint. Returns null when allowed (or
    /// unconstrained); a reason string when the version is disallowed. Uses the Phase 3 constraint grammar via
    /// the supplied <paramref name="satisfies"/> delegate so this stays free of the Terraform domain types.
    /// </summary>
    public static string? CheckTerraformVersion(
        OrgPolicy? policy, string? version, Func<string, string, bool> satisfies)
    {
        if (policy is null || string.IsNullOrWhiteSpace(policy.AllowedTerraformVersionConstraint))
            return null;
        if (string.IsNullOrWhiteSpace(version))
            return "The Terraform version could not be determined to check it against organisation policy.";

        return satisfies(version, policy.AllowedTerraformVersionConstraint)
            ? null
            : $"Terraform {version} is not permitted by organisation policy " +
              $"(allowed: {policy.AllowedTerraformVersionConstraint}).";
    }
}
