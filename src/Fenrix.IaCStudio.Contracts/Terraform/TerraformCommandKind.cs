namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// The typed commands Fenrix can run. Version/Init/Format/Validate shipped in Phase 3; Plan/Apply/Show
/// are the Phase 4 plan-and-apply-safety additions. Destroy and refresh-only are not separate kinds —
/// they are a <see cref="Plan"/> with <see cref="PlanOptions.Destroy"/> / <see cref="PlanOptions.RefreshOnly"/>
/// set, followed by an <see cref="Apply"/> of the saved plan. See docs/05-terraform-engine.md,
/// docs/06-plan-apply-safety.md.
/// </summary>
public enum TerraformCommandKind
{
    Version = 0,
    Init = 1,
    Format = 2,
    Validate = 3,

    /// <summary><c>terraform plan -out</c> (optionally <c>-destroy</c> / <c>-refresh-only</c>).</summary>
    Plan = 4,

    /// <summary><c>terraform apply</c> of an exact saved plan file.</summary>
    Apply = 5,

    /// <summary><c>terraform show -json &lt;plan&gt;</c> — read-only conversion of a saved plan for review.</summary>
    Show = 6
}
