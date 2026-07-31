using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed organisation policy: a single active row, read/edited by admins and evaluated (purely) against
/// actions. When enterprise mode is off there is no row and every evaluation is clear. The Terraform-version
/// check reuses the Phase 3 constraint grammar. Saves are audited. See docs/29-enterprise.md.
/// </summary>
public sealed class PolicyService(
    AppDbContext db,
    IEnterpriseConfig config,
    IAuthorizationService authorization,
    IAuditService audit) : IPolicyService
{
    private readonly AppDbContext _db = db;
    private readonly IEnterpriseConfig _config = config;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly IAuditService _audit = audit;

    public async Task<OrgPolicy?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.IsEnabled) return null;
        return await _db.OrgPolicies.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<OrgPolicySummary?> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        var policy = await GetActiveAsync(cancellationToken);
        return policy is null ? null : Map(policy);
    }

    public async Task<OrgPolicySummary> SaveAsync(
        SaveOrgPolicyRequest request, CancellationToken cancellationToken = default)
    {
        var authz = await _authorization.AuthorizeAsync(Permission.ManagePolicy, target: "organisation policy", cancellationToken: cancellationToken);
        if (!authz.Allowed)
            throw new UnauthorizedAccessException(authz.Reason ?? "You need the 'ManagePolicy' permission.");

        var policy = await _db.OrgPolicies.OrderByDescending(p => p.UpdatedAt).FirstOrDefaultAsync(cancellationToken);
        if (policy is null)
        {
            policy = new OrgPolicy();
            _db.OrgPolicies.Add(policy);
        }

        policy.RequireApprovalForProduction = request.RequireApprovalForProduction;
        policy.RequireApprovalForEnvironments = request.RequireApprovalForEnvironments
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct().ToList();
        policy.BlockProductionDestroy = request.BlockProductionDestroy;
        policy.RequirePrivateRepositories = request.RequirePrivateRepositories;
        policy.RequiredBranchForProduction = Clean(request.RequiredBranchForProduction);
        policy.AllowedTerraformVersionConstraint = ValidateConstraint(request.AllowedTerraformVersionConstraint);
        policy.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(
            AuditAction.PolicyChanged, Detail: "Organisation policy updated."), cancellationToken);

        return Map(policy);
    }

    public async Task<PolicyVerdict> EvaluateAsync(
        PolicyEvaluator.PolicyInputs inputs, CancellationToken cancellationToken = default)
        => PolicyEvaluator.Evaluate(await GetActiveAsync(cancellationToken), inputs);

    public async Task<string?> CheckTerraformVersionAsync(string? version, CancellationToken cancellationToken = default)
        => PolicyEvaluator.CheckTerraformVersion(await GetActiveAsync(cancellationToken), version, Satisfies);

    /// <summary>Phase 3 grammar: does <paramref name="version"/> satisfy the <paramref name="constraint"/>?</summary>
    private static bool Satisfies(string version, string constraint)
    {
        if (!TerraformVersion.TryParse(version, out var v) || v is null) return false;
        if (!TerraformVersionConstraint.TryParse(constraint, out var c) || c is null) return true; // unparsable ⇒ don't block
        return c.IsSatisfiedBy(v);
    }

    private static string? ValidateConstraint(string? constraint)
    {
        constraint = Clean(constraint);
        if (constraint is null) return null;
        if (!TerraformVersionConstraint.TryParse(constraint, out _))
            throw new InvalidOperationException($"'{constraint}' is not a valid Terraform version constraint.");
        return constraint;
    }

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static OrgPolicySummary Map(OrgPolicy p) => new(
        p.Id, p.RequireApprovalForProduction, p.RequireApprovalForEnvironments.ToList(),
        p.BlockProductionDestroy, p.RequirePrivateRepositories, p.RequiredBranchForProduction,
        p.AllowedTerraformVersionConstraint, p.UpdatedAt, p.UpdatedBy);
}
