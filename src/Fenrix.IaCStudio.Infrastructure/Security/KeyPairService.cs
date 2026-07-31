using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Security;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Security;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Security;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Terraform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// The end-to-end key-pair manager: import an existing key or generate one via Terraform, list/inspect,
/// build references, rotate/rename/delete, and perform the gated + audited private-key export. Private keys
/// are stored encrypted at rest (DPAPI) under the Fenrix data root via <see cref="KeyStore"/>; the database
/// holds only a <see cref="KeyPair"/> metadata row plus a <see cref="Domain.Security.SecretReference"/>
/// pointer — never the private bytes. See docs/28-key-pair-management.md, docs/11-secrets.md.
/// </summary>
public sealed class KeyPairService(
    AppDbContext db,
    KeyStore keyStore,
    KeyGenerationRunner generator,
    IProjectService projects,
    ISettingsService settings,
    ICommandHistoryStore history,
    IWorkspacePaths paths,
    IAuthorizationService authorization,
    ILogger<KeyPairService> logger) : IKeyPairService
{
    private const string AuditTool = "fenrix";

    private readonly AppDbContext _db = db;
    private readonly KeyStore _keyStore = keyStore;
    private readonly KeyGenerationRunner _generator = generator;
    private readonly IProjectService _projects = projects;
    private readonly ISettingsService _settings = settings;
    private readonly ICommandHistoryStore _history = history;
    private readonly IWorkspacePaths _paths = paths;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly ILogger<KeyPairService> _logger = logger;

    public async Task<IReadOnlyList<KeyPairSummary>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.Set<KeyPair>().AsNoTracking()
            .Where(k => k.ProjectId == projectId)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(MapSummary).ToList();
    }

    public async Task<KeyPairDetail?> GetAsync(Guid keyId, CancellationToken ct = default)
    {
        var key = await _db.Set<KeyPair>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        return key is null ? null : new KeyPairDetail(MapSummary(key), _keyStore.ToAbsolute(key.EncryptedFilePath));
    }

    // ---- import ----

    public async Task<KeyOperationResult> ImportAsync(ImportKeyRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return KeyOperationResult.Fail("Give the key a name.");
        if (string.IsNullOrWhiteSpace(request.SourceFilePath) || !File.Exists(request.SourceFilePath))
            return KeyOperationResult.Fail("Select an existing private-key file to import.");

        string text;
        try { text = await File.ReadAllTextAsync(request.SourceFilePath, ct); }
        catch (Exception ex) { return KeyOperationResult.Fail($"Could not read the key file: {ex.Message}"); }

        var material = SshPublicKeyReader.Read(text);
        if (material.Format == KeyMaterialFormat.Unknown && material.Public.OpenSshLine is null)
            return KeyOperationResult.Fail("Unrecognised private key. Provide a PEM, OpenSSH, or PuTTY (.ppk) private key.");

        var storedFormat = material.Format;
        var storedText = text;

        // A convertible (unencrypted RSA) PuTTY key is stored as PEM so it is directly usable in a
        // Terraform connection block; the public metadata derived from the PPK stays valid.
        if (material.Format == KeyMaterialFormat.Ppk)
        {
            var pem = PpkParser.TryConvertToPem(PpkParser.Parse(text));
            if (pem is not null)
            {
                storedText = pem;
                storedFormat = KeyMaterialFormat.Pem;
            }
        }

        var keyId = Guid.NewGuid();
        string relativePath;
        try { relativePath = _keyStore.WriteEncrypted(request.ProjectId, keyId, storedText); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write imported key to the secure store.");
            return KeyOperationResult.Fail($"Could not protect the key at rest: {ex.Message}");
        }

        var secretRef = NewSecretReference(request.Name, relativePath);
        var key = new KeyPair
        {
            Id = keyId,
            ProjectId = request.ProjectId,
            Name = request.Name.Trim(),
            Algorithm = material.Public.Algorithm,
            Bits = material.Public.Bits,
            Source = KeyPairSource.Imported,
            Format = storedFormat,
            PublicKeyOpenSsh = material.Public.OpenSshLine,
            Fingerprint = material.Public.Fingerprint,
            Comment = request.Comment ?? material.Public.Comment,
            EncryptedFilePath = relativePath,
            SecretReferenceId = secretRef.Id
        };

        _db.Set<SecretReference>().Add(secretRef);
        _db.Set<KeyPair>().Add(key);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Imported key {Name} ({Algorithm}) into project {Project}.", key.Name, key.Algorithm, key.ProjectId);
        return KeyOperationResult.Ok(keyId);
    }

    // ---- generate ----

    public Task<KeyOperationResult> GenerateAsync(
        GenerateKeyRequest request, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        GenerateCoreAsync(request, output, ct);

    private async Task<KeyOperationResult> GenerateCoreAsync(
        GenerateKeyRequest request, IProgress<ProcessOutputEvent>? output, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return KeyOperationResult.Fail("Give the key a name.");

        Guid? cloudConnectionId = null;
        if (request.RegisterInCloud)
        {
            if (request.EnvironmentId is null)
                return KeyOperationResult.Fail("Choose the environment whose cloud connection the key registers against.");
            var project = await _projects.GetAsync(request.ProjectId, ct);
            var env = project?.Environments.FirstOrDefault(e => e.Id == request.EnvironmentId);
            if (env is null)
                return KeyOperationResult.Fail("Environment not found.");
            if (env.CloudConnectionId is null)
                return KeyOperationResult.Fail("That environment has no bound cloud connection. Bind one before registering a key (authentication required).");
            cloudConnectionId = env.CloudConnectionId;
        }

        var keyId = Guid.NewGuid();
        var register = request.RegisterInCloud;
        var workingDir = register
            ? _keyStore.RegistrationDirectory(request.ProjectId, keyId)
            : Path.Combine(_paths.TempDirectory, "keygen", keyId.ToString("N"));

        var config = KeyGeneratorConfig.Build(request);
        var result = await _generator.RunAsync(request.ProjectId, workingDir, config, cloudConnectionId, output, ct);

        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.PrivateKeyPem))
        {
            CleanupWorkingDir(request.ProjectId, keyId, workingDir, register);
            return KeyOperationResult.Fail(result.Error ?? "Key generation failed.", result.RunId);
        }

        string relativePath;
        try { relativePath = _keyStore.WriteEncrypted(request.ProjectId, keyId, result.PrivateKeyPem!); }
        catch (Exception ex)
        {
            CleanupWorkingDir(request.ProjectId, keyId, workingDir, register);
            _logger.LogError(ex, "Generated a key but failed to protect it at rest.");
            return KeyOperationResult.Fail($"Generated the key but could not protect it at rest: {ex.Message}");
        }

        var secretRef = NewSecretReference(request.Name, relativePath);
        var key = new KeyPair
        {
            Id = keyId,
            ProjectId = request.ProjectId,
            Name = request.Name.Trim(),
            Algorithm = request.Algorithm,
            Bits = request.Algorithm == KeyAlgorithm.Rsa ? request.RsaBits : null,
            Source = KeyPairSource.Generated,
            Format = KeyMaterialFormat.Pem,
            PublicKeyOpenSsh = result.PublicKeyOpenSsh?.Trim(),
            Fingerprint = result.Fingerprint?.Trim(),
            Comment = request.Comment,
            EncryptedFilePath = relativePath,
            SecretReferenceId = secretRef.Id,
            CloudConnectionId = register ? cloudConnectionId : null,
            CloudKeyName = register ? (result.CloudKeyName ?? request.Name.Trim()) : null,
            RegistrationWorkingDir = register ? RelativeToDataRoot(workingDir) : null
        };

        _db.Set<SecretReference>().Add(secretRef);
        _db.Set<KeyPair>().Add(key);
        await _db.SaveChangesAsync(ct);

        // Local generation leaves state (which holds the private key) in a temp dir → remove it now that the
        // key is safely in the secure store. Registered keys keep their dir so the cloud object can be destroyed.
        if (!register)
            TryDeleteDirectory(workingDir);

        _logger.LogInformation("Generated key {Name} ({Algorithm}{Register}) in project {Project}.",
            key.Name, key.Algorithm, register ? ", registered" : "", key.ProjectId);
        return KeyOperationResult.Ok(keyId, result.RunId);
    }

    // ---- lifecycle ----

    public async Task<KeyOperationResult> RenameAsync(Guid keyId, string newName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return KeyOperationResult.Fail("Enter a new name.");
        var key = await _db.Set<KeyPair>().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null) return KeyOperationResult.Fail("Key not found.");
        key.Name = newName.Trim();
        await _db.SaveChangesAsync(ct);
        return KeyOperationResult.Ok(keyId);
    }

    public async Task<KeyOperationResult> RotateAsync(Guid keyId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        var key = await _db.Set<KeyPair>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null) return KeyOperationResult.Fail("Key not found.");

        var register = key.CloudConnectionId is not null;

        // De-register/remove the old key first so a registered rotation can reuse the same cloud key name.
        var deleted = await DeleteAsync(keyId, output, ct);
        if (!deleted.Succeeded)
            return KeyOperationResult.Fail($"Could not remove the existing key before rotating: {deleted.Error}");

        var request = new GenerateKeyRequest(
            key.ProjectId, key.Name, key.Algorithm,
            RsaBits: key.Bits ?? 4096,
            EcdsaCurve: null,
            RegisterInCloud: register,
            EnvironmentId: register ? await ResolveEnvironmentForConnectionAsync(key.ProjectId, key.CloudConnectionId!.Value, ct) : null,
            Comment: key.Comment);

        return await GenerateCoreAsync(request, output, ct);
    }

    public async Task<KeyOperationResult> DeleteAsync(Guid keyId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        var key = await _db.Set<KeyPair>().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null) return KeyOperationResult.Fail("Key not found.");

        // De-register the cloud object first; refuse to drop metadata if that fails (so the key isn't orphaned).
        if (key.RegistrationWorkingDir is not null)
        {
            var dir = _keyStore.ToAbsolute(key.RegistrationWorkingDir);
            var ok = await _generator.DestroyAsync(key.ProjectId, dir, key.CloudConnectionId, output, ct);
            if (!ok)
                return KeyOperationResult.Fail(
                    "Could not de-register the key from the cloud. Remove the cloud key pair manually, then delete again, or retry when the connection is available.");
            _keyStore.DeleteRegistrationDirectory(key.ProjectId, keyId);
        }

        _keyStore.Delete(key.EncryptedFilePath);
        if (key.SecretReferenceId is Guid refId)
        {
            var secret = await _db.Set<SecretReference>().FirstOrDefaultAsync(s => s.Id == refId, ct);
            if (secret is not null) _db.Set<SecretReference>().Remove(secret);
        }
        _db.Set<KeyPair>().Remove(key);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Deleted key {Name} from project {Project}.", key.Name, key.ProjectId);
        return KeyOperationResult.Ok(keyId);
    }

    // ---- gated export ----

    public async Task<KeyExportResult> ExportPrivateKeyAsync(Guid keyId, string confirmationPhrase, CancellationToken ct = default)
    {
        var key = await _db.Set<KeyPair>().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null) return KeyExportResult.Denied("Key not found.");

        // Enterprise RBAC: the current user must hold ExportPrivateKey for this project (allow-all when mode off).
        // This gate sits in front of the existing Settings toggle + typed-name confirmation. A denial self-audits.
        var authz = await _authorization.AuthorizeAsync(Permission.ExportPrivateKey, key.ProjectId, target: key.Name, cancellationToken: ct);
        if (!authz.Allowed)
        {
            await AuditAsync(key, "key-export-denied", "Denied", "not permitted", ct);
            return KeyExportResult.Denied(authz.Reason ?? "You are not permitted to export private keys.");
        }

        var allowed = await _settings.GetOrDefaultAsync(FenrixSettingKeys.AllowPrivateKeyExport, false, key.ProjectId, null, ct);
        if (!allowed)
        {
            await AuditAsync(key, "key-export-denied", "Denied", "export disabled", ct);
            return KeyExportResult.Denied("Private-key export is disabled. Enable it in Settings → Security to allow exports.");
        }

        if (!string.Equals(confirmationPhrase?.Trim(), key.Name, StringComparison.Ordinal))
        {
            await AuditAsync(key, "key-export-denied", "Denied", "confirmation mismatch", ct);
            return KeyExportResult.Denied($"Type the key name '{key.Name}' exactly to confirm the export.");
        }

        string privateKey;
        try { privateKey = _keyStore.ReadDecrypted(key.EncryptedFilePath); }
        catch (Exception ex)
        {
            await AuditAsync(key, "key-export-denied", "Failed", "decrypt failed", ct);
            _logger.LogError(ex, "Failed to decrypt key {KeyId} for export.", keyId);
            return KeyExportResult.Denied($"Could not decrypt the key: {ex.Message}");
        }

        key.LastExportedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await AuditAsync(key, "key-export", "Succeeded", "exported", ct);

        return new KeyExportResult(true, privateKey, key.Format, null);
    }

    // ---- reference snippet ----

    public async Task<KeyReferenceSnippet?> BuildReferenceAsync(Guid keyId, KeyReferenceKind kind, CancellationToken ct = default)
    {
        var key = await _db.Set<KeyPair>().AsNoTracking().FirstOrDefaultAsync(k => k.Id == keyId, ct);
        if (key is null) return null;
        var securePath = _keyStore.ToAbsolute(key.EncryptedFilePath);
        return KeyReferenceSnippetBuilder.Build(MapSummary(key), securePath, kind);
    }

    // ---- helpers ----

    private SecretReference NewSecretReference(string name, string relativePath) => new()
    {
        Provider = SecretProvider.WindowsDpapi,
        ReferenceKey = relativePath,
        DisplayName = $"Key: {name}"
    };

    private async Task<Guid?> ResolveEnvironmentForConnectionAsync(Guid projectId, Guid connectionId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct);
        return project?.Environments.FirstOrDefault(e => e.CloudConnectionId == connectionId)?.Id;
    }

    private void CleanupWorkingDir(Guid projectId, Guid keyId, string workingDir, bool register)
    {
        if (register) _keyStore.DeleteRegistrationDirectory(projectId, keyId);
        else TryDeleteDirectory(workingDir);
    }

    private void TryDeleteDirectory(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not remove key working directory {Dir}.", dir); }
    }

    private string RelativeToDataRoot(string absolute)
    {
        var rel = Path.GetRelativePath(_paths.DataRoot, absolute);
        return rel.Replace('\\', '/');
    }

    /// <summary>Records a redacted audit row for a key reveal/export (allowed or denied). Never logs the key.</summary>
    private async Task AuditAsync(KeyPair key, string command, string status, string detail, CancellationToken ct)
    {
        try
        {
            var run = new CommandRun
            {
                ProjectId = key.ProjectId,
                Tool = AuditTool,
                Command = command,
                RedactedArguments = $"key='{key.Name}' fingerprint={key.Fingerprint ?? "n/a"} ({detail})",
                WorkingDirectory = string.Empty,
                Status = TerraformRunStatusRunning
            };
            await _history.RecordStartAsync(run, ct);
            await _history.RecordCompletionAsync(run.Id, status, null, DateTimeOffset.UtcNow, null, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write key-export audit row.");
        }
    }

    private const string TerraformRunStatusRunning = "Running";

    private static KeyPairSummary MapSummary(KeyPair k) => new(
        k.Id, k.ProjectId, k.Name, k.Algorithm, k.Bits, k.Source, k.Format,
        k.PublicKeyOpenSsh, k.Fingerprint, k.Comment,
        k.CloudConnectionId is not null, k.CloudKeyName, k.CreatedAt, k.LastExportedAt);
}
