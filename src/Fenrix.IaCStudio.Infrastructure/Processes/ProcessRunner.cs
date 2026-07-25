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

    public Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default) =>
        RunCoreAsync(
            request.ExecutablePath, request.WorkingDirectory, request.Arguments,
            request.EnvironmentVariables, request.Command, request.RequiresInteractiveTerminal, output, ct);

    public Task<ProcessResult> RunAsync(
        ProcessStartRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default) =>
        RunCoreAsync(
            request.ExecutablePath, request.WorkingDirectory, request.Arguments,
            request.EnvironmentVariables, request.CommandLabel, request.RequiresInteractiveTerminal, output, ct);

    private async Task<ProcessResult> RunCoreAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environmentVariables,
        string commandLabel,
        bool requiresInteractiveTerminal,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,          // required for redirection and to avoid the shell entirely
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = requiresInteractiveTerminal
        };

        // Arguments go through ArgumentList so the OS receives them as a proper argv — no shell parsing,
        // no injection surface. Never build a single command string here.
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in environmentVariables)
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
                throw new InvalidOperationException($"Failed to start '{executablePath}'.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not start {Executable} in {WorkingDir}", executablePath, workingDirectory);
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
            Path.GetFileName(executablePath), commandLabel, exitCode,
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
