namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>Where a discovered Terraform binary came from. See docs/05-terraform-engine.md.</summary>
public enum TerraformExecutableSource
{
    /// <summary>Explicitly configured in Settings (<c>terraform.executable</c>).</summary>
    Configured = 0,

    /// <summary>Resolved from the system <c>PATH</c>.</summary>
    Path = 1,

    /// <summary>Managed by Fenrix under the Tools directory (future).</summary>
    Managed = 2
}
