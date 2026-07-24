namespace Fenrix.IaCStudio.Domain.Files;

/// <summary>
/// The kind of change captured in a <see cref="FileVersion"/>. See docs/21-file-history-recovery.md.
/// </summary>
public enum FileChangeKind
{
    /// <summary>The file first appeared (Fenrix editor create, import, or watcher-detected add).</summary>
    Created = 0,

    /// <summary>Existing tracked content changed.</summary>
    Updated = 1,

    /// <summary>The file moved/renamed; history follows via <see cref="FileVersion.FileIdentityId"/>.</summary>
    Renamed = 2,

    /// <summary>The file was removed on disk (detected by the reconciler); last content retained for recovery.</summary>
    DeletedDetected = 3,

    /// <summary>A previous version was written back to disk via recovery.</summary>
    Restored = 4
}
