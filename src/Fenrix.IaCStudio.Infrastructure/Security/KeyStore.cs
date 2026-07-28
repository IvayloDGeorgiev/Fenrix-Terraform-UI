using System.Text;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Security;

namespace Fenrix.IaCStudio.Infrastructure.Security;

/// <summary>
/// Places managed private keys in a secure, app-managed folder outside any project
/// (<c>&lt;dataRoot&gt;\Data\keys\&lt;projectId&gt;\</c>) so they can never be committed, and encrypts them at
/// rest with DPAPI via <see cref="IKeyProtector"/>. The database stores only the <em>relative</em> path
/// returned here; the plaintext is produced transiently on read and dropped by the caller. See
/// docs/28-key-pair-management.md, docs/11-secrets.md.
/// </summary>
public sealed class KeyStore(IWorkspacePaths paths, IKeyProtector protector)
{
    private const string KeysFolder = "keys";
    private readonly IWorkspacePaths _paths = paths;
    private readonly IKeyProtector _protector = protector;

    /// <summary>The absolute per-project keys directory (created on demand).</summary>
    public string ProjectKeysDirectory(Guid projectId)
    {
        var dir = Path.Combine(_paths.DataDirectory, KeysFolder, projectId.ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Encrypts and writes a private key; returns its path relative to the data root (for the DB).</summary>
    public string WriteEncrypted(Guid projectId, Guid keyId, string privateKeyText)
    {
        var dir = ProjectKeysDirectory(projectId);
        var absolute = Path.Combine(dir, keyId.ToString("N") + ".key");
        var cipher = _protector.Protect(Encoding.UTF8.GetBytes(privateKeyText));
        File.WriteAllBytes(absolute, cipher);
        return ToRelative(absolute);
    }

    /// <summary>Reads and decrypts a stored private key back to its original text.</summary>
    public string ReadDecrypted(string relativePath)
    {
        var absolute = ToAbsolute(relativePath);
        var cipher = File.ReadAllBytes(absolute);
        return Encoding.UTF8.GetString(_protector.Unprotect(cipher));
    }

    /// <summary>Deletes a stored private key file (idempotent).</summary>
    public void Delete(string relativePath)
    {
        try
        {
            var absolute = ToAbsolute(relativePath);
            if (File.Exists(absolute)) File.Delete(absolute);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    /// <summary>The absolute self-contained Terraform working directory kept for a cloud-registered key.</summary>
    public string RegistrationDirectory(Guid projectId, Guid keyId)
    {
        var dir = Path.Combine(ProjectKeysDirectory(projectId), keyId.ToString("N") + "-tf");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public void DeleteRegistrationDirectory(Guid projectId, Guid keyId)
    {
        try
        {
            var dir = Path.Combine(ProjectKeysDirectory(projectId), keyId.ToString("N") + "-tf");
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    public string ToAbsolute(string relativePath) =>
        Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(_paths.DataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private string ToRelative(string absolutePath)
    {
        var rel = Path.GetRelativePath(_paths.DataRoot, absolutePath);
        return rel.Replace('\\', '/');
    }
}
