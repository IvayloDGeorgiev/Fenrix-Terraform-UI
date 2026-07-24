using System.Text;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Runs one Terraform command through the process runner while recording a redacted history row and,
/// optionally, a raw log file — capturing the full output and stdout for the caller to parse. Shared by
/// the Phase 4 plan/apply services so they record history identically to the Phase 3 executor.
///
/// <para><paramref name="captureLog"/> is <c>false</c> for <c>-json</c> commands (<c>show -json</c>,
/// <c>apply -json</c>): their output can contain unredacted sensitive values, which must never be written
/// to a normal log file (docs/06-plan-apply-safety.md, docs/11-secrets.md). Human-readable runs (plan)
/// are safe to log because Terraform already masks sensitive values as "(sensitive value)".</para>
/// </summary>
public sealed class TerraformProcessCoordinator(
    IProcessRunner runner,
    ICommandHistoryStore history,
    IWorkspacePaths paths,
    ILogger<TerraformProcessCoordinator> logger)
{
    private const string Tool = "terraform";

    private readonly IProcessRunner _runner = runner;
    private readonly ICommandHistoryStore _history = history;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<TerraformProcessCoordinator> _logger = logger;

    /// <summary>The captured outcome of a coordinated run.</summary>
    public sealed record CoordinatedRun(
        Guid RunId,
        ProcessResult Process,
        string FullOutput,
        string StandardOutput,
        string? LogPath);

    public async Task<CoordinatedRun> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        bool captureLog,
        CancellationToken ct = default)
    {
        var redactedArgs = ArgumentRedactor.RedactArguments(request.Arguments);
        var run = new CommandRun
        {
            ProjectId = request.ProjectId == Guid.Empty ? null : request.ProjectId,
            EnvironmentId = request.EnvironmentId == Guid.Empty ? null : request.EnvironmentId,
            Tool = Tool,
            Command = request.Command,
            RedactedArguments = string.Join(' ', redactedArgs),
            WorkingDirectory = request.WorkingDirectory,
            Status = TerraformRunStatus.Running.ToString()
        };
        await _history.RecordStartAsync(run, ct);

        var full = new StringBuilder();
        var stdout = new StringBuilder();
        var capturing = new CapturingProgress(output, full, stdout);

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(request, capturing, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Terraform {Command} failed to run.", request.Command);
            await _history.RecordCompletionAsync(run.Id, TerraformRunStatus.Failed.ToString(), null, DateTimeOffset.Now, null, ct);
            throw;
        }

        var logPath = captureLog ? await WriteLogAsync(run.Id, full.ToString(), ct) : null;

        var status = result.Cancelled
            ? TerraformRunStatus.Cancelled
            : result.ExitCode == 0 ? TerraformRunStatus.Succeeded : TerraformRunStatus.Failed;
        await _history.RecordCompletionAsync(run.Id, status.ToString(), result.ExitCode, result.CompletedAt, logPath, ct);

        return new CoordinatedRun(run.Id, result, full.ToString(), stdout.ToString(), logPath);
    }

    private async Task<string?> WriteLogAsync(Guid runId, string content, CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(_paths.LogsDirectory, "terraform");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{runId:N}.log");
            await File.WriteAllTextAsync(path, content, ct);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write Terraform log for run {RunId}.", runId);
            return null;
        }
    }

    private sealed class CapturingProgress(IProgress<ProcessOutputEvent>? downstream, StringBuilder full, StringBuilder stdout)
        : IProgress<ProcessOutputEvent>
    {
        public void Report(ProcessOutputEvent value)
        {
            lock (full)
            {
                full.AppendLine(value.Text);
                if (value.Stream == OutputStream.Stdout)
                    stdout.AppendLine(value.Text);
            }
            downstream?.Report(value);
        }
    }
}
