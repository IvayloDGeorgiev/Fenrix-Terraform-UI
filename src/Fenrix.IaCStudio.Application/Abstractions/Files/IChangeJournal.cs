using Fenrix.IaCStudio.Domain.Files;

namespace Fenrix.IaCStudio.Application.Abstractions.Files;

/// <summary>
/// Short-lived record of writes Fenrix performs itself, so the watcher can recognise its own events
/// and not surface them as external changes. Entries expire after a short window.
/// See docs/04-filesystem-sync.md (loop prevention).
/// </summary>
public interface IChangeJournal
{
    /// <summary>Records an expected application-generated change at <paramref name="fullPath"/>.</summary>
    void Record(string fullPath, FileChangeKind kind, long expectedLength, string? expectedHash);

    /// <summary>
    /// Returns true (and consumes the entry) when a watcher event matches a recent app-generated
    /// change, meaning it should be suppressed.
    /// </summary>
    bool TryConsume(string fullPath, long actualLength);
}
