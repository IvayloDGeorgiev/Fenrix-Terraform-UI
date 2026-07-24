namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>The typed command screens delivered in Phase 3. See docs/05-terraform-engine.md.</summary>
public enum TerraformCommandKind
{
    Version = 0,
    Init = 1,
    Format = 2,
    Validate = 3
}
