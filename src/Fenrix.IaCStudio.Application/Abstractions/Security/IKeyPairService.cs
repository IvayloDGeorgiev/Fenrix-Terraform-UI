using Fenrix.IaCStudio.Contracts.Security;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Security;

/// <summary>
/// Manages a project's SSH / EC2 key pairs end to end: import an existing key or generate one via Terraform,
/// list/inspect them, copy the public half or secure path, rotate/rename/delete, and perform the gated,
/// audited private-key export. Private keys are stored encrypted at rest (DPAPI) outside the project folder;
/// only metadata + a secret reference live in the database. See docs/28-key-pair-management.md, docs/11-secrets.md.
/// </summary>
public interface IKeyPairService
{
    /// <summary>All key pairs for a project, newest first.</summary>
    Task<IReadOnlyList<KeyPairSummary>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>A single key with its resolved secure private-key path, or null if not found.</summary>
    Task<KeyPairDetail?> GetAsync(Guid keyId, CancellationToken ct = default);

    /// <summary>
    /// Imports an existing private key from disk: copies it (encrypted) into the secure store, derives the
    /// public key + fingerprint where possible, and records metadata. The original file is left untouched.
    /// </summary>
    Task<KeyOperationResult> ImportAsync(ImportKeyRequest request, CancellationToken ct = default);

    /// <summary>
    /// Generates a new key pair via Terraform (<c>tls_private_key</c>, optionally <c>aws_key_pair</c>),
    /// capturing the sensitive private key straight into the secure store — no cloud-console round-trip. The
    /// optional progress stream carries the (redaction-safe) command output for the UI console.
    /// </summary>
    Task<KeyOperationResult> GenerateAsync(
        GenerateKeyRequest request, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>Renames a key (metadata only; the stored material is unchanged).</summary>
    Task<KeyOperationResult> RenameAsync(Guid keyId, string newName, CancellationToken ct = default);

    /// <summary>
    /// Rotates a key: generates a fresh key pair with the same name/settings and, once it is safely stored,
    /// deletes the old one (de-registering its cloud object first if it was registered).
    /// </summary>
    Task<KeyOperationResult> RotateAsync(Guid keyId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>
    /// Deletes a key: removes the encrypted file, the secret reference, and the metadata row; if the key was
    /// registered in a cloud, its object is de-registered (<c>terraform destroy</c>) first.
    /// </summary>
    Task<KeyOperationResult> DeleteAsync(Guid keyId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>
    /// Reveals/exports the private key. Gated: refused unless the security setting allows export AND the
    /// supplied confirmation phrase matches the key name. Every attempt (allowed or denied) is audited.
    /// </summary>
    Task<KeyExportResult> ExportPrivateKeyAsync(Guid keyId, string confirmationPhrase, CancellationToken ct = default);

    /// <summary>Builds a ready-to-paste reference snippet (connection/provisioner/aws_key_pair/public/path) for a key.</summary>
    Task<KeyReferenceSnippet?> BuildReferenceAsync(Guid keyId, KeyReferenceKind kind, CancellationToken ct = default);
}
