using Fenrix.IaCStudio.Domain.Security;

namespace Fenrix.IaCStudio.Contracts.Security;

/// <summary>
/// A key pair as shown in the project's Keys list. Never carries private-key material — only the public
/// half and metadata. See docs/28-key-pair-management.md.
/// </summary>
public sealed record KeyPairSummary(
    Guid Id,
    Guid ProjectId,
    string Name,
    KeyAlgorithm Algorithm,
    int? Bits,
    KeyPairSource Source,
    KeyMaterialFormat Format,
    string? PublicKeyOpenSsh,
    string? Fingerprint,
    string? Comment,
    bool IsCloudRegistered,
    string? CloudKeyName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastExportedAt);

/// <summary>
/// Everything the Keys detail view needs: the summary plus the resolved absolute secure path of the
/// encrypted private key (for a <c>connection</c>/<c>provisioner</c> reference). Still no private bytes.
/// </summary>
public sealed record KeyPairDetail(
    KeyPairSummary Summary,
    string SecurePrivateKeyPath);

/// <summary>Request to import an existing private key from disk into the secure store.</summary>
public sealed record ImportKeyRequest(
    Guid ProjectId,
    string Name,
    string SourceFilePath,
    string? Comment = null);

/// <summary>
/// Request to generate a new key pair via Terraform. When <see cref="RegisterInCloud"/> is true a matching
/// <c>aws_key_pair</c> is also created against the given environment's bound connection; otherwise generation
/// is purely local (<c>tls_private_key</c> only, no cloud round-trip).
/// </summary>
public sealed record GenerateKeyRequest(
    Guid ProjectId,
    string Name,
    KeyAlgorithm Algorithm = KeyAlgorithm.Rsa,
    int RsaBits = 4096,
    string? EcdsaCurve = null,       // e.g. "P256" | "P384" | "P521" (tls provider curve name)
    bool RegisterInCloud = false,
    Guid? EnvironmentId = null,      // required when RegisterInCloud is true (supplies the cloud connection)
    string? Comment = null);

/// <summary>Outcome of a key operation (import/generate/rotate/delete). Carries a user-facing message on failure.</summary>
public sealed record KeyOperationResult(
    bool Succeeded,
    Guid? KeyId,
    string? Error,
    Guid? RunId = null)
{
    public static KeyOperationResult Ok(Guid keyId, Guid? runId = null) => new(true, keyId, null, runId);
    public static KeyOperationResult Fail(string error, Guid? runId = null) => new(false, null, error, runId);
}

/// <summary>
/// The result of a gated private-key export/reveal. The <see cref="PrivateKey"/> is present only on success
/// and is intended to be handed straight to the UI (copy-to-clipboard / save-as) and then dropped — it is
/// never logged or persisted. See docs/11-secrets.md.
/// </summary>
public sealed record KeyExportResult(
    bool Succeeded,
    string? PrivateKey,
    KeyMaterialFormat Format,
    string? Error)
{
    public static KeyExportResult Denied(string error) => new(false, null, KeyMaterialFormat.Unknown, error);
}

/// <summary>Which kind of HCL reference the snippet builder should emit for a managed key.</summary>
public enum KeyReferenceKind
{
    /// <summary>A <c>connection { … private_key = file("&lt;path&gt;") }</c> block for a provisioner.</summary>
    Connection = 0,

    /// <summary>A full <c>provisioner "remote-exec"</c> with an embedded connection block.</summary>
    Provisioner = 1,

    /// <summary>An <c>aws_key_pair</c> resource wiring the public key.</summary>
    AwsKeyPair = 2,

    /// <summary>Just the OpenSSH public key line.</summary>
    PublicKey = 3,

    /// <summary>Just the secure absolute path of the private key.</summary>
    SecurePath = 4
}

/// <summary>A ready-to-paste HCL (or plain value) snippet referencing a managed key, plus a label for the UI.</summary>
public sealed record KeyReferenceSnippet(KeyReferenceKind Kind, string Label, string Content);
