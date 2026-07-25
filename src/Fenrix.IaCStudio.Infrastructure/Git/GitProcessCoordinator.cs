using System.Text;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Git;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Git;

/// <summary>
/// Runs one Git command through the shared safe process runner while recording a redacted history row
/// (<c>Tool = "git"</c>) and a log file. Mirrors <c>TerraformProcessCoordinator</c> so Git records history
/// identically to Terraform. Credentials are redacted from the stored arguments; a local-only remote
/// posture is enforced with <c>GIT_TERMINAL_PROMPT=0</c> so remote commands fail fast instead of blocking on
/// an interactive prompt. See docs/08-git-engine.md, docs/15-logging-auditing.md, docs/23-command-transparency.md.
/// </summary>
public sealed class GitProcessCoordinator(
    IProcessRunner runner,
    ICommandHistoryStore history,
    IWorkspacePaths paths,
    ILogger<GitProcessCoordinator> logger)
{
    private const string Tool = "git";

    private readonly IProcessRunner _runner = runner;
    private readonly ICommandHistoryStore _history = history;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<GitProcessCoordinator> _logger = logger;

    /// <summary>The captured outcome of a coordinated Git run.</summary>
    public sealed record CoordinatedRun(
        Guid RunId,
        ProcessResult Process,
        string FullOutput,
        string StandardOutput,
        string StandardError,
        string? LogPath)
    {
        public bool Succeeded => Process.Succeeded;
    }

    public async Task<CoordinatedRun> RunAsync(
        GitCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        bool captureLog = true,
        CancellationToken ct = default)
    {
        var redactedArgs = GitCommandPreviewBuilder.RedactArguments(request.Arguments);
        var run = new CommandRun
        {
            ProjectId = request.ProjectId == Guid.Empty ? null : request.ProjectId,
            EnvironmentId = null,
            Tool = Tool,
            Command = request.Command,
            RedactedArguments = string.Join(' ', redactedArgs),
            WorkingDirectory = request.WorkingDirectory,
            Status = TerraformRunStatus.Running.ToString()
        };
        await _history.RecordStartAsync(run, ct);

        var env = new Dictionary<string, string>(request.EnvironmentVariables)
        {
            // Local-only posture: never block on a credential prompt (docs/08-git-engine.md).
            ["GIT_TERMINAL_PROMPT"] = "0"
        };

        var start = new ProcessStartRequest(
            request.ExecutablePath, request.WorkingDirectory, request.Arguments, env, $"git {request.Command}");

        var full = new StringBuilder();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var capturing = new CapturingProgress(output, full, stdout, stderr);

        ProcessResult result;
        try
        {
            result = await _runner.RunAsync(start, capturing, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "git {Command} failed to run.", request.Command);
            await _history.RecordCompletionAsync(run.Id, TerraformRunStatus.Failed.ToString(), null, DateTimeOffset.Now, null, ct);
            throw;
        }

        var logPath = captureLog ? await WriteLogAsync(run.Id, full.ToString(), ct) : null;

        var status = result.Cancelled
            ? TerraformRunStatus.Cancelled
            : result.ExitCode == 0 ? TerraformRunStatus.Succeeded : TerraformRunStatus.Failed;
        await _history.RecordCompletionAsync(run.Id, status.ToString(), result.ExitCode, result.CompletedAt, logPath, ct);

        return new CoordinatedRun(run.Id, result, full.ToString(), stdout.ToString(), stderr.ToString(), logPath);
    }

    private async Task<string?> WriteLogAsync(Guid runId, string content, CancellationToken ct)
    {
        try
        {
            var dir = Path.Combine(_paths.LogsDirectory, "git");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"{runId:N}.log");
            await File.WriteAllTextAsync(path, content, ct);
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write git log for run {RunId}.", runId);
            return null;
        }
    }

    private sealed class CapturingProgress(
        IProgress<ProcessOutputEvent>? downstream, StringBuilder full, StringBuilder stdout, StringBuilder stderr)
        : IProgress<ProcessOutputEvent>
    {
        public void Report(ProcessOutputEvent value)
        {
            lock (full)
            {
                full.AppendLine(value.Text);
                if (value.Stream == OutputStream.Stdout) stdout.AppendLine(value.Text);
                else stderr.AppendLine(value.Text);
            }
            downstream?.Report(value);
        }
    }
}
