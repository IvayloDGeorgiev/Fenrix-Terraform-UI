namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// Organisation-wide governance switches, enforced <em>in addition to</em> every existing gate —
/// policy can only tighten, never loosen. A single active row per store (the org's policy).
/// With enterprise mode off there is no row and nothing is added. See docs/29-enterprise.md.
/// </summary>
public sealed class OrgPolicy
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Force the approval gate on production environments (replaces the local self-ack).</summary>
    public bool RequireApprovalForProduction { get; set; } = true;

    /// <summary>Additional environment names (case-insensitive) that also require approval.</summary>
    public List<string> RequireApprovalForEnvironments { get; set; } = [];

    /// <summary>Refuse a destroy against a production environment outright.</summary>
    public bool BlockProductionDestroy { get; set; }

    /// <summary>Warn/block binding a public repository (plans/state carry secrets).</summary>
    public bool RequirePrivateRepositories { get; set; }

    /// <summary>Live may only deploy from this branch when set (empty = unrestricted).</summary>
    public string? RequiredBranchForProduction { get; set; }

    /// <summary>
    /// Allowed Terraform versions. When non-empty, the resolved binary must satisfy it; expressed as a
    /// <c>required_version</c>-style constraint string (Phase 3 grammar), AND-ed with the project's own.
    /// </summary>
    public string? AllowedTerraformVersionConstraint { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string UpdatedBy { get; set; } = string.Empty;
}
