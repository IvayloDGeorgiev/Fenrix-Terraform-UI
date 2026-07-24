using System.Text;
using Fenrix.IaCStudio.Application.Files;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Pure helpers for the plan-integrity hashes that gate apply. A saved plan records a combined
/// configuration hash and a provider-lock hash; if either changes after the plan was produced, the plan
/// is invalidated and cannot be applied. See docs/06-plan-apply-safety.md and ADR-0003.
/// </summary>
public static class PlanIntegrity
{
    /// <summary>File name of the provider dependency lock, hashed for plan integrity.</summary>
    public const string LockFileName = ".terraform.lock.hcl";

    // Suffixes that make a file part of the Terraform configuration for integrity purposes.
    private static readonly string[] ConfigurationSuffixes =
        [".tf", ".tf.json", ".tfvars", ".tfvars.json", ".hcl"];

    /// <summary>
    /// True when a project-relative path is a Terraform configuration file that should contribute to the
    /// configuration hash. The lock file is hashed separately and is intentionally excluded here.
    /// </summary>
    public static bool IsConfigurationFile(string relativePathOrName)
    {
        if (string.IsNullOrWhiteSpace(relativePathOrName))
            return false;

        var name = relativePathOrName.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0) name = name[(slash + 1)..];

        if (name.Equals(LockFileName, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var suffix in ConfigurationSuffixes)
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// Deterministically combines per-file hashes into a single configuration hash. Files are ordered by
    /// their normalized relative path so the result is stable regardless of enumeration order; each entry
    /// contributes both its path and content hash so renames and edits both change the result.
    /// </summary>
    public static string CombineConfigHashes(IEnumerable<KeyValuePair<string, string>> relativePathToSha256)
    {
        var ordered = relativePathToSha256
            .Select(kv => (Path: kv.Key.Replace('\\', '/'), Hash: kv.Value))
            .OrderBy(x => x.Path, StringComparer.Ordinal)
            .ToList();

        var sb = new StringBuilder();
        foreach (var (path, hash) in ordered)
            sb.Append(path).Append('\0').Append(hash).Append('\n');

        return FileHashing.Sha256Hex(Encoding.UTF8.GetBytes(sb.ToString()));
    }

    /// <summary>
    /// Compares recomputed hashes against the stored ones and returns a human-readable invalidation reason,
    /// or <c>null</c> when the plan is still valid. Git provenance is not compared here (Phase 5).
    /// </summary>
    public static string? DetermineInvalidation(
        string? storedConfigHash, string? currentConfigHash,
        string? storedLockHash, string? currentLockHash)
    {
        if (!string.Equals(storedConfigHash, currentConfigHash, StringComparison.Ordinal))
            return "The configuration files changed after this plan was created.";
        if (!string.Equals(storedLockHash, currentLockHash, StringComparison.Ordinal))
            return "The provider lock file (.terraform.lock.hcl) changed after this plan was created.";
        return null;
    }
}
