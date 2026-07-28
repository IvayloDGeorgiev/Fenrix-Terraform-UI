namespace Fenrix.IaCStudio.Domain.Security;

/// <summary>
/// Metadata for a project-scoped SSH / EC2 key pair. The private key itself never lives here: it is stored
/// encrypted at rest (Windows DPAPI) under <c>Data\keys\&lt;projectId&gt;\&lt;keyId&gt;</c>, outside the project
/// folder so it can never be committed, and this record holds only a pointer to it plus the non-secret public
/// half. See docs/28-key-pair-management.md and docs/11-secrets.md.
/// </summary>
public sealed class KeyPair
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The project this key belongs to. Keys are managed per project.</summary>
    public Guid ProjectId { get; set; }

    /// <summary>User-facing name; also used as the default cloud key name and the <c>key_name</c> when registering.</summary>
    public string Name { get; set; } = string.Empty;

    public KeyAlgorithm Algorithm { get; set; } = KeyAlgorithm.Unknown;

    /// <summary>Key size in bits where meaningful (RSA); null for Ed25519 or when unknown.</summary>
    public int? Bits { get; set; }

    public KeyPairSource Source { get; set; } = KeyPairSource.Imported;

    /// <summary>The encoding of the stored private-key bytes (so export writes back the exact original form).</summary>
    public KeyMaterialFormat Format { get; set; } = KeyMaterialFormat.Unknown;

    /// <summary>The public key in OpenSSH single-line form (<c>ssh-rsa AAAA… comment</c>), when derivable.</summary>
    public string? PublicKeyOpenSsh { get; set; }

    /// <summary>SHA-256 fingerprint of the public key, OpenSSH form (<c>SHA256:base64nopad</c>), when derivable.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Optional comment carried by the public key / imported file.</summary>
    public string? Comment { get; set; }

    /// <summary>
    /// Path (relative to the Fenrix data root) of the DPAPI-encrypted private-key blob. The database never
    /// holds the private bytes — only this pointer and the <see cref="SecretReferenceId"/>.
    /// </summary>
    public string EncryptedFilePath { get; set; } = string.Empty;

    /// <summary>The DPAPI secret reference describing where/how the private key is protected.</summary>
    public Guid? SecretReferenceId { get; set; }

    // ---- optional cloud registration (Generated keys that were also registered, e.g. aws_key_pair) ----

    /// <summary>The cloud connection the key was registered against, if any (else purely local).</summary>
    public Guid? CloudConnectionId { get; set; }

    /// <summary>The key name as registered in the cloud (e.g. the AWS key-pair name), if registered.</summary>
    public string? CloudKeyName { get; set; }

    /// <summary>
    /// Path (relative to the data root) of the self-contained Terraform working directory kept for a
    /// registered key so its cloud object can be de-registered (<c>destroy</c>) on delete. Null for local keys.
    /// </summary>
    public string? RegistrationWorkingDir { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Last time the private key was revealed/exported (for the audit trail surfaced in the UI).</summary>
    public DateTimeOffset? LastExportedAt { get; set; }
}
