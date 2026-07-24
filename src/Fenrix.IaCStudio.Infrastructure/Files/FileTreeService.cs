using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Files;
using Fenrix.IaCStudio.Domain.Files;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Files;

/// <summary>
/// Disk-backed file-tree operations. Mutations use atomic writes, journal themselves for loop
/// prevention, prefer the Recycle Bin for deletes, and record versions for recovery.
/// See docs/04-filesystem-sync.md and docs/21-file-history-recovery.md.
/// </summary>
public sealed class FileTreeService(
    IChangeJournal journal,
    IRecycleBin recycleBin,
    IFileHistoryStore history,
    ISettingsService settings,
    ILogger<FileTreeService> logger) : IFileTreeService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System
    };

    private readonly IChangeJournal _journal = journal;
    private readonly IRecycleBin _recycleBin = recycleBin;
    private readonly IFileHistoryStore _history = history;
    private readonly ISettingsService _settings = settings;
    private readonly ILogger<FileTreeService> _logger = logger;

    public Task<FileTreeNode> GetTreeAsync(Guid projectId, string projectRoot, CancellationToken ct = default)
    {
        var root = new FileTreeNode
        {
            Name = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectRoot)),
            RelativePath = string.Empty,
            FullPath = projectRoot,
            IsDirectory = true
        };
        if (Directory.Exists(projectRoot))
            BuildChildren(projectRoot, projectRoot, root, ct);
        return Task.FromResult(root);
    }

    private static void BuildChildren(string projectRoot, string dir, FileTreeNode parent, CancellationToken ct)
    {
        IEnumerable<string> dirs, files;
        try
        {
            dirs = Directory.EnumerateDirectories(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            files = Directory.EnumerateFiles(dir).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var d in dirs)
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileName(d);
            var ignored = FileTrackingPolicy.IsIgnoredDirectory(name);
            var node = new FileTreeNode
            {
                Name = name,
                RelativePath = FileTrackingPolicy.ToRelative(projectRoot, d),
                FullPath = d,
                IsDirectory = true,
                IsIgnored = ignored
            };
            // Do not descend into ignored/machine directories (avoids event storms & huge trees).
            if (!ignored)
                BuildChildren(projectRoot, d, node, ct);
            parent.Children.Add(node);
        }

        foreach (var f in files)
        {
            FileInfo info;
            try { info = new FileInfo(f); }
            catch { continue; }

            parent.Children.Add(new FileTreeNode
            {
                Name = info.Name,
                RelativePath = FileTrackingPolicy.ToRelative(projectRoot, f),
                FullPath = f,
                IsDirectory = false,
                SizeBytes = info.Exists ? info.Length : 0,
                ModifiedAt = info.Exists ? info.LastWriteTimeUtc : null
            });
        }
    }

    public async Task CreateFileAsync(Guid projectId, string projectRoot, string relativePath, string? initialContent = null, CancellationToken ct = default)
    {
        var full = ResolveInside(projectRoot, relativePath);
        if (File.Exists(full))
            throw new IOException($"A file already exists at '{relativePath}'.");

        await WriteInternalAsync(projectId, projectRoot, relativePath, initialContent ?? string.Empty, FileChangeKind.Created, ct);
    }

    public Task CreateFolderAsync(string projectRoot, string relativePath, CancellationToken ct = default)
    {
        var full = ResolveInside(projectRoot, relativePath);
        Directory.CreateDirectory(full);
        return Task.CompletedTask;
    }

    public Task WriteFileAsync(Guid projectId, string projectRoot, string relativePath, string content, CancellationToken ct = default)
        => WriteInternalAsync(projectId, projectRoot, relativePath, content, FileChangeKind.Updated, ct);

    private async Task WriteInternalAsync(Guid projectId, string projectRoot, string relativePath, string content, FileChangeKind kind, CancellationToken ct)
    {
        var full = ResolveInside(projectRoot, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        var bytes = Utf8NoBom.GetBytes(content);
        var hash = FileHashing.Sha256Hex(bytes);

        // Journal before writing so the watcher recognises our own event.
        _journal.Record(full, kind, bytes.LongLength, hash);

        var temp = full + ".fenrixtmp";
        await File.WriteAllBytesAsync(temp, bytes, ct);
        File.Move(temp, full, overwrite: true);

        await _history.RecordAsync(new FileChange
        {
            ProjectId = projectId,
            RelativePath = FileTrackingPolicy.ToRelative(projectRoot, full),
            FullPath = full,
            ChangeKind = kind,
            Origin = ChangeOrigin.FenrixEditor
        }, ct);
    }

    public async Task RenameAsync(Guid projectId, string projectRoot, string relativePath, string newName, CancellationToken ct = default)
    {
        if (newName.Contains('/') || newName.Contains('\\'))
            throw new ArgumentException("A name cannot contain path separators.", nameof(newName));

        var oldRel = relativePath.Replace('\\', '/');
        var parent = Path.GetDirectoryName(oldRel)?.Replace('\\', '/');
        var newRel = string.IsNullOrEmpty(parent) ? newName : $"{parent}/{newName}";
        await MoveAsync(projectId, projectRoot, relativePath, newRel, ct);
    }

    public async Task MoveAsync(Guid projectId, string projectRoot, string relativePath, string newRelativePath, CancellationToken ct = default)
    {
        var source = ResolveInside(projectRoot, relativePath);
        var dest = ResolveInside(projectRoot, newRelativePath);
        if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        if (Directory.Exists(source))
        {
            await MoveDirectoryAsync(projectId, projectRoot, source, dest, ct);
            return;
        }

        if (!File.Exists(source))
            throw new FileNotFoundException($"'{relativePath}' does not exist.", source);

        var length = new FileInfo(source).Length;
        _journal.Record(source, FileChangeKind.DeletedDetected, -1, null);
        _journal.Record(dest, FileChangeKind.Renamed, length, null);
        File.Move(source, dest, overwrite: false);

        await _history.RecordAsync(new FileChange
        {
            ProjectId = projectId,
            RelativePath = FileTrackingPolicy.ToRelative(projectRoot, dest),
            PreviousRelativePath = FileTrackingPolicy.ToRelative(projectRoot, source),
            FullPath = dest,
            ChangeKind = FileChangeKind.Renamed,
            Origin = ChangeOrigin.FenrixEditor
        }, ct);
    }

    /// <summary>Moves a directory, journalling and recording each tracked descendant as a rename so
    /// history identities follow and the reconciler does not surface false deletions.</summary>
    private async Task MoveDirectoryAsync(Guid projectId, string projectRoot, string source, string dest, CancellationToken ct)
    {
        var sourceRel = FileTrackingPolicy.ToRelative(projectRoot, source);
        var destRel = FileTrackingPolicy.ToRelative(projectRoot, dest);

        var descendants = Directory.EnumerateFiles(source, "*", RecursiveOptions)
            .Select(f => FileTrackingPolicy.ToRelative(projectRoot, f))
            .Where(FileTrackingPolicy.IsVersioned)
            .ToList();

        // Journal the old locations as (our own) deletions before the move.
        foreach (var relOld in descendants)
            _journal.Record(ResolveInside(projectRoot, relOld), FileChangeKind.DeletedDetected, -1, null);

        Directory.Move(source, dest);

        foreach (var relOld in descendants)
        {
            var relNew = destRel + relOld[sourceRel.Length..];
            var newFull = ResolveInside(projectRoot, relNew);
            long len = 0;
            try { len = new FileInfo(newFull).Length; } catch { /* best effort */ }
            _journal.Record(newFull, FileChangeKind.Renamed, len, null);

            await _history.RecordAsync(new FileChange
            {
                ProjectId = projectId,
                RelativePath = relNew,
                PreviousRelativePath = relOld,
                FullPath = newFull,
                ChangeKind = FileChangeKind.Renamed,
                Origin = ChangeOrigin.FenrixEditor
            }, ct);
        }
    }

    public async Task DeleteAsync(Guid projectId, string projectRoot, string relativePath, CancellationToken ct = default)
    {
        var full = ResolveInside(projectRoot, relativePath);
        var rel = FileTrackingPolicy.ToRelative(projectRoot, full);

        var isTrackedFile = File.Exists(full) && FileTrackingPolicy.IsVersioned(rel);
        if (isTrackedFile)
        {
            var allowDelete = await _settings.GetOrDefaultAsync(FenrixSettingKeys.AllowInAppDelete, false, projectId, null, ct);
            if (!allowDelete)
                throw new InvalidOperationException(
                    "In-app deletion of tracked files is disabled. Enable it in Settings → Security, or delete the file in Explorer (it stays recoverable).");
        }

        // Record last-known content before removing, so it stays recoverable.
        if (isTrackedFile)
        {
            await _history.RecordAsync(new FileChange
            {
                ProjectId = projectId,
                RelativePath = rel,
                FullPath = full,
                ChangeKind = FileChangeKind.Updated,
                Origin = ChangeOrigin.FenrixEditor
            }, ct);
        }

        _journal.Record(full, FileChangeKind.DeletedDetected, -1, null);
        await _recycleBin.SendToRecycleBinAsync(full, ct);

        if (isTrackedFile)
        {
            await _history.RecordAsync(new FileChange
            {
                ProjectId = projectId,
                RelativePath = rel,
                ChangeKind = FileChangeKind.DeletedDetected,
                Origin = ChangeOrigin.FenrixEditor
            }, ct);
        }

        _logger.LogInformation("Deleted {Path} (recycle bin) for project {Project}", rel, projectId);
    }

    /// <summary>Resolves a project-relative path to an absolute path, guarding against escaping the root.</summary>
    private static string ResolveInside(string projectRoot, string relativePath)
    {
        var rootFull = Path.GetFullPath(projectRoot);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path escapes the project root.");
        return combined;
    }
}
