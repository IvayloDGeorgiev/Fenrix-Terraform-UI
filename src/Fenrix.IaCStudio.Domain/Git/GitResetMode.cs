namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// How far <c>git reset &lt;target&gt;</c> unwinds. Soft moves only HEAD (keeps index + working tree); Mixed
/// (the default) also resets the index but keeps the working tree; Hard also discards working-tree changes
/// and is therefore destructive. See docs/08-git-engine.md (Advanced + Safety).
/// </summary>
public enum GitResetMode
{
    /// <summary>Move HEAD only; index and working tree are left untouched (changes become staged).</summary>
    Soft = 0,

    /// <summary>Move HEAD and reset the index; keep the working tree (changes become unstaged). Git's default.</summary>
    Mixed = 1,

    /// <summary>Move HEAD, reset the index, and overwrite the working tree — discards uncommitted work.</summary>
    Hard = 2
}
