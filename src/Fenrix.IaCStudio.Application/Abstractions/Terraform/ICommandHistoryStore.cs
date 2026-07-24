using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Execution;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Persists redacted command-run history. Only redacted arguments are stored; raw sensitive output is
/// never written to the database (it goes to a log file referenced by <c>OutputLogPath</c>). See
/// docs/15-logging-auditing.md and docs/23-command-transparency.md.
/// </summary>
public interface ICommandHistoryStore
{
    /// <summary>Records the start of a run and returns the persisted entity (with its generated id).</summary>
    Task<CommandRun> RecordStartAsync(CommandRun run, CancellationToken ct = default);

    /// <summary>Marks a run complete with its status, exit code, completion time, and log path.</summary>
    Task RecordCompletionAsync(
        Guid runId,
        string status,
        int? exitCode,
        DateTimeOffset completedAt,
        string? outputLogPath,
        CancellationToken ct = default);

    /// <summary>Returns the most recent runs, newest first, optionally scoped to a project.</summary>
    Task<IReadOnlyList<CommandRunSummary>> GetRecentAsync(Guid? projectId = null, int limit = 50, CancellationToken ct = default);
}
