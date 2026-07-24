using System.Collections.Concurrent;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Contracts.Files;
using Fenrix.IaCStudio.Domain.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Files;

/// <summary>
/// Watches project files with a <c>FileSystemWatcher</c> plus a periodic reconciliation sweep, which
/// corrects anything the watcher combined, reordered, duplicated, or missed. App-generated writes are
/// suppressed via the change journal; external changes are recorded to history and surfaced.
/// See docs/04-filesystem-sync.md.
/// </summary>
public sealed class ProjectFileSynchronizer(
    IServiceScopeFactory scopeFactory,
    IChangeJournal journal,
    ILogger<ProjectFileSynchronizer> logger) : IProjectFileSynchronizer, IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(15);

    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly IChangeJournal _journal = journal;
    private readonly ILogger<ProjectFileSynchronizer> _logger = logger;

    private readonly ConcurrentDictionary<string, Watch> _watches = new(StringComparer.OrdinalIgnoreCase);

    public event Action<FileSystemChangeEvent>? ExternalChangeDetected;

    private sealed class Watch : IDisposable
    {
        public required Guid ProjectId { get; init; }
        public required string Root { get; init; }
        public required FileSystemWatcher Watcher { get; init; }
        public required Timer Debounce { get; init; }
        public required Timer Periodic { get; init; }
        public readonly SemaphoreSlim Gate = new(1, 1);
        public Dictionary<string, Snapshot> Index = new(StringComparer.OrdinalIgnoreCase);

        public void Dispose()
        {
            Watcher.EnableRaisingEvents = false;
            Watcher.Dispose();
            Debounce.Dispose();
            Periodic.Dispose();
            Gate.Dispose();
        }
    }

    private readonly record struct Snapshot(long Size, DateTime LastWriteUtc);

    public async Task StartAsync(Guid projectId, string projectPath, CancellationToken ct = default)
    {
        var key = Key(projectPath);
        if (_watches.ContainsKey(key))
            return;

        var root = Path.GetFullPath(projectPath);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"'{projectPath}' does not exist.");

        var watcher = new FileSystemWatcher(root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                         | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024
        };

        var watch = new Watch
        {
            ProjectId = projectId,
            Root = root,
            Watcher = watcher,
            Debounce = new Timer(_ => _ = ReconcileSafe(key), null, Timeout.Infinite, Timeout.Infinite),
            Periodic = new Timer(_ => _ = ReconcileSafe(key), null, ReconcileInterval, ReconcileInterval)
        };

        watcher.Changed += (_, _) => Bump(watch);
        watcher.Created += (_, _) => Bump(watch);
        watcher.Deleted += (_, _) => Bump(watch);
        watcher.Renamed += (_, _) => Bump(watch);
        watcher.Error += (_, e) =>
        {
            _logger.LogWarning(e.GetException(), "Watcher error for {Root}; forcing reconcile", root);
            Bump(watch);
        };

        watch.Index = await Task.Run(() => BuildIndex(root), ct);
        _watches[key] = watch;
        watcher.EnableRaisingEvents = true;

        _logger.LogInformation("Started file synchronizer for {Root} ({Count} tracked files)", root, watch.Index.Count);
    }

    public Task StopAsync(string projectPath)
    {
        if (_watches.TryRemove(Key(projectPath), out var watch))
        {
            watch.Dispose();
            _logger.LogInformation("Stopped file synchronizer for {Root}", watch.Root);
        }
        return Task.CompletedTask;
    }

    public async Task RescanAsync(Guid projectId, string projectPath, CancellationToken ct = default)
    {
        var key = Key(projectPath);
        if (!_watches.ContainsKey(key))
            await StartAsync(projectId, projectPath, ct);
        else
            await ReconcileSafe(key);
    }

    private void Bump(Watch watch) => watch.Debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);

    private async Task ReconcileSafe(string key)
    {
        if (!_watches.TryGetValue(key, out var watch))
            return;
        if (!await watch.Gate.WaitAsync(0))
            return; // a reconcile is already running; the periodic timer will catch anything new
        try
        {
            await ReconcileAsync(watch);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reconcile failed for {Root}", watch.Root);
        }
        finally
        {
            watch.Gate.Release();
        }
    }

    private async Task ReconcileAsync(Watch watch)
    {
        if (!Directory.Exists(watch.Root))
            return;

        var current = BuildIndex(watch.Root);
        var previous = watch.Index;
        var deltas = new List<FileChange>();
        var events = new List<FileSystemChangeEvent>();

        // Added or updated.
        foreach (var (rel, snap) in current)
        {
            var full = Path.Combine(watch.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!previous.TryGetValue(rel, out var old))
            {
                if (_journal.TryConsume(full, snap.Size)) continue; // our own create
                deltas.Add(Change(watch.ProjectId, rel, full, FileChangeKind.Created));
                events.Add(Event(watch.ProjectId, rel, FileChangeKind.Created));
            }
            else if (old.Size != snap.Size || old.LastWriteUtc != snap.LastWriteUtc)
            {
                if (_journal.TryConsume(full, snap.Size)) continue; // our own write
                deltas.Add(Change(watch.ProjectId, rel, full, FileChangeKind.Updated));
                events.Add(Event(watch.ProjectId, rel, FileChangeKind.Updated));
            }
        }

        // Deleted.
        foreach (var (rel, _) in previous)
        {
            if (current.ContainsKey(rel))
                continue;
            var full = Path.Combine(watch.Root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (_journal.TryConsume(full, 0)) continue; // our own delete/rename-source
            deltas.Add(Change(watch.ProjectId, rel, null, FileChangeKind.DeletedDetected));
            events.Add(Event(watch.ProjectId, rel, FileChangeKind.DeletedDetected));
        }

        watch.Index = current;

        if (deltas.Count == 0)
            return;

        using (var scope = _scopeFactory.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IFileHistoryStore>();
            foreach (var delta in deltas)
                await history.RecordAsync(delta);
        }

        foreach (var evt in events)
        {
            try { ExternalChangeDetected?.Invoke(evt); }
            catch (Exception ex) { _logger.LogWarning(ex, "External-change subscriber threw"); }
        }

        _logger.LogInformation("Reconciled {Root}: {Count} external change(s)", watch.Root, deltas.Count);
    }

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System
    };

    private static Dictionary<string, Snapshot> BuildIndex(string root)
    {
        var index = new Dictionary<string, Snapshot>(StringComparer.OrdinalIgnoreCase);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(root, "*", RecursiveOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return index;
        }

        foreach (var file in files)
        {
            var rel = FileTrackingPolicy.ToRelative(root, file);
            if (!FileTrackingPolicy.IsVersioned(rel))
                continue;
            try
            {
                var info = new FileInfo(file);
                if (info.Exists)
                    index[rel] = new Snapshot(info.Length, info.LastWriteTimeUtc);
            }
            catch (IOException) { /* transient; next sweep picks it up */ }
        }
        return index;
    }

    private static FileChange Change(Guid projectId, string rel, string? full, FileChangeKind kind) => new()
    {
        ProjectId = projectId,
        RelativePath = rel,
        FullPath = full,
        ChangeKind = kind,
        Origin = ChangeOrigin.External
    };

    private static FileSystemChangeEvent Event(Guid projectId, string rel, FileChangeKind kind) => new()
    {
        ProjectId = projectId,
        RelativePath = rel,
        ChangeKind = kind,
        IsExternal = true
    };

    private static string Key(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public void Dispose()
    {
        foreach (var watch in _watches.Values)
            watch.Dispose();
        _watches.Clear();
    }
}
