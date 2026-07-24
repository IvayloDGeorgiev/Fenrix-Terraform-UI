namespace Fenrix.IaCStudio.Application.Files;

/// <summary>
/// Central rules for which paths Fenrix watches and versions. Shared by the import scanner,
/// filesystem synchronizer, file-tree service, and history store so behaviour stays consistent.
/// See docs/04-filesystem-sync.md and docs/21-file-history-recovery.md.
/// </summary>
public static class FileTrackingPolicy
{
    /// <summary>Directories that are noisy/machine-generated and are not deeply monitored or versioned.</summary>
    public static readonly IReadOnlySet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".terraform", "node_modules", "bin", "obj"
    };

    /// <summary>
    /// File extensions whose content is captured in version history by default. Includes Terraform plan
    /// (<c>.tfplan</c>) and state (<c>.tfstate</c>) files so every plan and state change is version-tracked
    /// (see docs/06-plan-apply-safety.md). Note: plan and state files can contain sensitive values in
    /// plaintext — they are versioned deliberately and belong only in private, secured repositories.
    /// </summary>
    public static readonly IReadOnlySet<string> VersionedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".tf", ".tfvars", ".hcl", ".json", ".md", ".gitignore", ".gitattributes", ".txt", ".yaml", ".yml",
        ".tfplan", ".tfstate"
    };

    /// <summary>Extensions treated as Terraform configuration for import detection.</summary>
    public static readonly IReadOnlySet<string> TerraformExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".tf", ".tfvars", ".hcl"
    };

    /// <summary>True when any segment of the relative path is an ignored directory.</summary>
    public static bool IsUnderIgnoredDirectory(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
            return false;

        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        // the final segment is the file/dir name itself; check ancestor segments
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (IgnoredDirectories.Contains(segments[i]))
                return true;
        }

        // also treat the node itself as ignored when it is a known machine dir
        return segments.Length > 0 && IgnoredDirectories.Contains(segments[^1]);
    }

    /// <summary>True when the directory name should not be descended into.</summary>
    public static bool IsIgnoredDirectory(string directoryName) => IgnoredDirectories.Contains(directoryName);

    /// <summary>True when a file's content should be captured in version history.</summary>
    public static bool IsVersioned(string relativePath)
    {
        if (IsUnderIgnoredDirectory(relativePath))
            return false;

        var name = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries) is { Length: > 0 } s
            ? s[^1]
            : relativePath;

        // dot-files like .gitignore have no "extension" in the usual sense
        if (VersionedExtensions.Contains(name))
            return true;

        var ext = Path.GetExtension(name);
        return VersionedExtensions.Contains(ext);
    }

    /// <summary>Normalises an absolute path to a forward-slash project-relative path.</summary>
    public static string ToRelative(string projectRoot, string absolutePath)
    {
        var rel = Path.GetRelativePath(projectRoot, absolutePath);
        return rel.Replace('\\', '/');
    }
}
