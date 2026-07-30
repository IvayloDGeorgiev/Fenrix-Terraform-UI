using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Reads/writes the single active organisation policy and evaluates it against an action. Policy can only
/// tighten (add approval or a hard block); when enterprise mode is off there is no policy and everything is
/// clear. Consumed by the deploy flow and apply preflight. See docs/29-enterprise.md.
/// </summary>
public interface IPolicyService
{
    /// <summary>The active policy, or null when none is configured / enterprise mode is off.</summary>
    Task<OrgPolicy?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<OrgPolicySummary?> GetSummaryAsync(CancellationToken cancellationToken = default);
    Task<OrgPolicySummary> SaveAsync(SaveOrgPolicyRequest request, CancellationToken cancellationToken = default);

    /// <summary>Evaluates the active policy against an action's facts (returns clear when policy is off).</summary>
    Task<PolicyVerdict> EvaluateAsync(
        PolicyEvaluator.PolicyInputs inputs, CancellationToken cancellationToken = default);

    /// <summary>Checks a resolved Terraform version against the org allow-constraint; null when permitted.</summary>
    Task<string?> CheckTerraformVersionAsync(string? version, CancellationToken cancellationToken = default);
}
