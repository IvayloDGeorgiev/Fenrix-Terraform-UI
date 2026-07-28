namespace Fenrix.IaCStudio.Application.Abstractions.Security;

/// <summary>
/// Encrypts and decrypts small local values at rest using an OS-native, per-user mechanism (Windows DPAPI).
/// Used to protect managed private-key files (docs/28-key-pair-management.md, item (6) of docs/11-secrets.md).
/// The plaintext is only ever held transiently by the caller and discarded after use.
/// </summary>
public interface IKeyProtector
{
    /// <summary>Whether this machine/OS can protect values (DPAPI is Windows-only).</summary>
    bool IsSupported { get; }

    /// <summary>Encrypts plaintext bytes for the current user. The output is opaque and safe to store on disk.</summary>
    byte[] Protect(byte[] plaintext);

    /// <summary>Decrypts bytes previously produced by <see cref="Protect"/>. Throws if the blob is not decryptable.</summary>
    byte[] Unprotect(byte[] ciphertext);
}
