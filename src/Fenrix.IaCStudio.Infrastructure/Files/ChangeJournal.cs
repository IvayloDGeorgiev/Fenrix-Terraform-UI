using System.Collections.Concurrent;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Domain.Files;

namespace Fenrix.IaCStudio.Infrastructure.Files;

/// <summary>
/// In-memory, short-lived journal of app-generated writes for watcher loop prevention.
/// Thread-safe; entries expire after a short window. See docs/04-filesystem-sync.md.
/// </summary>
public sealed class ChangeJournal : IChangeJournal
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private sealed record Entry(FileChangeKind Kind, long ExpectedLength, string? ExpectedHash, DateTimeOffset At);

    // Multiple app writes can target the same path in quick succession; keep a small queue per path.
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Entry>> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public void Record(string fullPath, FileChangeKind kind, long expectedLength, string? expectedHash)
    {
        var key = Normalize(fullPath);
        var queue = _entries.GetOrAdd(key, _ => new ConcurrentQueue<Entry>());
        queue.Enqueue(new Entry(kind, expectedLength, expectedHash, DateTimeOffset.UtcNow));
    }

    public bool TryConsume(string fullPath, long actualLength)
    {
        var key = Normalize(fullPath);
        if (!_entries.TryGetValue(key, out var queue))
            return false;

        var now = DateTimeOffset.UtcNow;
        while (queue.TryPeek(out var entry))
        {
            if (now - entry.At > Window)
            {
                queue.TryDequeue(out _); // expired; drop and keep scanning
                continue;
            }

            // A matching length (or a delete marker) is treated as our own change.
            if (entry.Kind is FileChangeKind.DeletedDetected || entry.ExpectedLength == actualLength || entry.ExpectedLength < 0)
            {
                queue.TryDequeue(out _);
                if (queue.IsEmpty)
                    _entries.TryRemove(key, out _);
                return true;
            }

            // Front entry is recent but doesn't match this event; leave it and treat event as external.
            return false;
        }

        _entries.TryRemove(key, out _);
        return false;
    }

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
