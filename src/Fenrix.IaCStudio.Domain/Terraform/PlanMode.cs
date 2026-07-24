namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// What a saved plan represents. All three are produced by <c>terraform plan -out</c> and applied via the
/// exact-saved-plan path (ADR-0003); the mode drives the review framing and the apply safety warnings.
/// See docs/06-plan-apply-safety.md and docs/25-execution-lifecycle.md.
/// </summary>
public enum PlanMode
{
    /// <summary>A normal create/update/destroy plan (<c>terraform plan -out</c>).</summary>
    Normal = 0,

    /// <summary>A full teardown plan (<c>terraform plan -destroy -out</c>).</summary>
    Destroy = 1,

    /// <summary>A drift-only plan that reconciles state without changing infrastructure (<c>-refresh-only</c>).</summary>
    RefreshOnly = 2
}
