namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>The active organisation policy as shown/edited in the admin UI. Null-ish defaults ⇒ nothing enforced.</summary>
public sealed record OrgPolicySummary(
    Guid Id,
    bool RequireApprovalForProduction,
    IReadOnlyList<string> RequireApprovalForEnvironments,
    bool BlockProductionDestroy,
    bool RequirePrivateRepositories,
    string? RequiredBranchForProduction,
    string? AllowedTerraformVersionConstraint,
    DateTimeOffset UpdatedAt,
    string UpdatedBy);

/// <summary>Create/update the organisation policy.</summary>
public sealed record SaveOrgPolicyRequest(
    bool RequireApprovalForProduction,
    IReadOnlyList<string> RequireApprovalForEnvironments,
    bool BlockProductionDestroy,
    bool RequirePrivateRepositories,
    string? RequiredBranchForProduction,
    string? AllowedTerraformVersionConstraint);
