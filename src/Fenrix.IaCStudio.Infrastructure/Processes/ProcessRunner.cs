using System.Diagnostics;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Processes;

/// <summary>
/// Safe external-process runner. Arguments are passed via <see cref="ProcessStartInfo.ArgumentList"/>
/// (never a concatenated shell string), stdout/stderr are redirected and streamed line-by-line, the
/// working directory and environment are set explicitly, and cancellation kills the entire process
/// tree. See docs/05-terraform-engine.md and docs/17-testing-strategy.md (command-injection tests).
/// </summary>
public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    private readonly ILogger<ProcessRunner> _logger = logger;

    public async Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,          // required for redirection and to avoid the shell entirely
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.RequiresInteractiveTerminal
        };

        // Arguments go through ArgumentList so the OS receives them as a proper argv — no shell parsing,
        // no injection surface. Never build a single command string here.
        foreach (var arg in request.Arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in request.EnvironmentVariables)
            psi.Environment[key] = value;

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var startedAt = DateTimeOffset.Now;
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.Exited += (_, _) => exited.TrySetResult();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output?.Report(ProcessOutputEvent.Out(e.Data));
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                output?.Report(ProcessOutputEvent.Error(e.Data));
        };

        try
        {
            if (!process.Start())
                throw new InvalidOperationException($"Failed to start '{request.ExecutablePath}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start {Executable} in {WorkingDir}", request.ExecutablePath, request.WorkingDirectory);
            throw;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var cancelled = false;
        try
        {
            using (ct.Register(() => TryKillTree(process)))
            {
                await exited.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            cancelled = ct.IsCancellationRequested;
            // Let any buffered output flush before we read ExitCode.
            try { process.WaitForExit(); } catch { /* already gone */ }
        }

        var completedAt = DateTimeOffset.Now;
        var exitCode = SafeExitCode(process);

        _logger.LogInformation(
            "Process {Executable} {Command} exited {ExitCode} in {Ms} ms (cancelled={Cancelled})",
            Path.GetFileName(request.ExecutablePath), request.Command, exitCode,
            (completedAt - startedAt).TotalMilliseconds, cancelled);

        return new ProcessResult(exitCode, cancelled, startedAt, completedAt);
    }

    private void TryKillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                _logger.LogWarning("Cancellation requested — killing process tree {Pid}", SafeId(process));
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Process already exited between the check and the kill; nothing to do.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }

    private static int SafeId(Process process)
    {
        try { return process.Id; }
        catch { return -1; }
    }
}
