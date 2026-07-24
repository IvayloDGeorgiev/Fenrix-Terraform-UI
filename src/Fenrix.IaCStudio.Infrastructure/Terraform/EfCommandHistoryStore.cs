using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// EF Core store for redacted command-run history. Persists <see cref="CommandRun"/> rows with only
/// redacted arguments; raw output lives in a log file referenced by <c>OutputLogPath</c>. Works against
/// any connected provider (SQLite/SQL Server). See docs/15-logging-auditing.md.
/// </summary>
public sealed class EfCommandHistoryStore(
    AppDbContext db,
    ILogger<EfCommandHistoryStore> logger) : ICommandHistoryStore
{
    private readonly AppDbContext _db = db;
    private readonly ILogger<EfCommandHistoryStore> _logger = logger;

    public async Task<CommandRun> RecordStartAsync(CommandRun run, CancellationToken ct = default)
    {
        _db.CommandRuns.Add(run);
        await _db.SaveChangesAsync(ct);
        return run;
    }

    public async Task RecordCompletionAsync(
        Guid runId,
        string status,
        int? exitCode,
        DateTimeOffset completedAt,
        string? outputLogPath,
        CancellationToken ct = default)
    {
        var run = await _db.CommandRuns.FirstOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
        {
            _logger.LogWarning("Command run {RunId} not found when recording completion.", runId);
            return;
        }

        run.Status = status;
        run.ExitCode = exitCode;
        run.CompletedAt = completedAt;
        run.OutputLogPath = outputLogPath;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<CommandRunSummary>> GetRecentAsync(Guid? projectId = null, int limit = 50, CancellationToken ct = default)
    {
        var query = _db.CommandRuns.AsNoTracking();
        if (projectId is not null)
            query = query.Where(r => r.ProjectId == projectId);

        var rows = await query
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);

        return rows.Select(r => new CommandRunSummary(
            r.Id, r.ProjectId, r.EnvironmentId, r.Tool, r.Command, r.RedactedArguments,
            r.WorkingDirectory, r.Status, r.ExitCode, r.StartedAt, r.CompletedAt, r.OutputLogPath)).ToList();
    }
}
