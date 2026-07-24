using Fenrix.IaCStudio.Contracts.Files;

namespace Fenrix.IaCStudio.Application.Abstractions.Files;

/// <summary>
/// Watches a project's files with a <c>FileSystemWatcher</c> combined with periodic reconciliation,
/// suppressing app-generated changes via the change journal and recording external changes to history.
/// See docs/04-filesystem-sync.md.
/// </summary>
public interface IProjectFileSynchronizer
{
    /// <summary>Raised for reconciled, externally-originated changes (app-generated events are suppressed).</summary>
    event Action<FileSystemChangeEvent>? ExternalChangeDetected;

    /// <summary>Starts watching + periodic reconciliation for a project. Idempotent per project path.</summary>
    Task StartAsync(Guid projectId, string projectPath, CancellationToken ct = default);

    /// <summary>Stops watching a project.</summary>
    Task StopAsync(string projectPath);

    /// <summary>Forces a full directory reconciliation now, emitting any missed deltas.</summary>
    Task RescanAsync(Guid projectId, string projectPath, CancellationToken ct = default);
}
