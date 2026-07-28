namespace Fenrix.IaCStudio.Domain.Security;

/// <summary>How a managed key pair came to exist in Fenrix. See docs/28-key-pair-management.md.</summary>
public enum KeyPairSource
{
    /// <summary>An existing private key the user imported from disk.</summary>
    Imported = 0,

    /// <summary>Generated on the backend via Terraform (<c>tls_private_key</c>), optionally registered in a cloud.</summary>
    Generated = 1
}
