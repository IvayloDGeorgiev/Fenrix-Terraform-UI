namespace Fenrix.IaCStudio.Domain.Files;

/// <summary>Where a captured file change originated. See docs/21-file-history-recovery.md.</summary>
public enum ChangeOrigin
{
    /// <summary>The change was made through Fenrix's own editor / file operations.</summary>
    FenrixEditor = 0,

    /// <summary>The change was observed on disk (Explorer, git, another editor) by the watcher/reconciler.</summary>
    External = 1,

    /// <summary>The change was recorded while importing/registering an existing project.</summary>
    Import = 2,

    /// <summary>The change is the result of restoring a previous version.</summary>
    Restore = 3
}
