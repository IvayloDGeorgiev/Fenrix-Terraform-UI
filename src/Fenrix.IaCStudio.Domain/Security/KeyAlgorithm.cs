namespace Fenrix.IaCStudio.Domain.Security;

/// <summary>The public-key algorithm of a managed key pair. See docs/28-key-pair-management.md.</summary>
public enum KeyAlgorithm
{
    /// <summary>Algorithm could not be determined from the key material.</summary>
    Unknown = 0,
    Rsa = 1,
    Ecdsa = 2,
    Ed25519 = 3
}
