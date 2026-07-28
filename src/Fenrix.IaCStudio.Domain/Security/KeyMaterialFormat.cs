namespace Fenrix.IaCStudio.Domain.Security;

/// <summary>
/// The on-disk encoding of a stored private key. Fenrix keeps the imported bytes verbatim (encrypted at
/// rest) so the original file round-trips exactly on export/use. See docs/28-key-pair-management.md.
/// </summary>
public enum KeyMaterialFormat
{
    Unknown = 0,

    /// <summary>PEM (PKCS#1 / PKCS#8 / SEC1) — <c>-----BEGIN … PRIVATE KEY-----</c>.</summary>
    Pem = 1,

    /// <summary>OpenSSH private key — <c>-----BEGIN OPENSSH PRIVATE KEY-----</c>.</summary>
    OpenSsh = 2,

    /// <summary>PuTTY private key (<c>.ppk</c>, v2 or v3).</summary>
    Ppk = 3
}
