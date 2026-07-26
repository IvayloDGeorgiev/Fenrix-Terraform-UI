using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// Locates and runs a cloud CLI (<c>az</c>, <c>aws</c>, <c>gcloud</c>) through the shared
/// <see cref="IProcessRunner"/>. Two Windows realities are handled here so adapters stay simple:
/// <list type="bullet">
///   <item><description>PATH resolution honours <c>PATHEXT</c>, so a bare <c>az</c> resolves to <c>az.cmd</c>.</description></item>
///   <item><description><c>.cmd</c>/<c>.bat</c> shims cannot be launched via <c>CreateProcess</c> directly, so they are
///   routed through <c>cmd.exe /c</c> — still using <see cref="ProcessStartInfo.ArgumentList"/> (each token passed
///   individually), never a concatenated shell string, preserving the engine-wide no-shell-string invariant.</description></item>
/// </list>
/// Output is captured in full for JSON parsing; the runner already redirects and streams line-by-line. This is
/// read-only account inspection — it never logs a raw file. See docs/10-cloud-integrations.md, docs/11-secrets.md.
/// </summary>
public static class CloudCli
{
    /// <summary>The captured outcome of a CLI invocation.</summary>
    public sealed record CliRun(bool Started, int ExitCode, string StdOut, string StdErr)
    {
        public bool Succeeded => Started && ExitCode == 0;
    }

    // PATH+PATHEXT resolution is stable for a process lifetime; cache it.
    private static readonly ConcurrentDictionary<string, (string Launcher, IReadOnlyList<string> Prefix)?> Resolved =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the tool is present on PATH (resolves the launcher).</summary>
    public static bool IsAvailable(string tool) => Locate(tool) is not null;

    /// <summary>
    /// Runs <paramref name="tool"/> with <paramref name="args"/> and the given process-scoped environment,
    /// capturing stdout/stderr. Returns <see cref="CliRun.Started"/> = false when the CLI is not installed.
    /// </summary>
    public static async Task<CliRun> RunAsync(
        IProcessRunner runner,
        string tool,
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string>? env,
        CancellationToken ct)
    {
        var located = Locate(tool);
        if (located is null)
            return new CliRun(false, -1, string.Empty, $"'{tool}' was not found on PATH.");

        var (launcher, prefix) = located.Value;
        var fullArgs = new List<string>(prefix.Count + args.Count);
        fullArgs.AddRange(prefix);
        fullArgs.AddRange(args);

        var request = new ProcessStartRequest(
            launcher,
            WorkingDirectory: Environment.CurrentDirectory,
            fullArgs,
            env ?? new Dictionary<string, string>(0),
            CommandLabel: tool);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var capture = new CaptureProgress(stdout, stderr);

        try
        {
            var result = await runner.RunAsync(request, capture, ct);
            return new CliRun(true, result.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return new CliRun(false, -1, string.Empty, ex.Message);
        }
    }

    private static (string Launcher, IReadOnlyList<string> Prefix)? Locate(string tool) =>
        Resolved.GetOrAdd(tool, static t => LocateCore(t));

    private static (string Launcher, IReadOnlyList<string> Prefix)? LocateCore(string tool)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        // On non-Windows a bare name is fine; CreateProcess searches PATH for the exact file.
        if (!isWindows)
            return FindOnPath(tool, [""]) is { } unix ? (unix, Array.Empty<string>()) : null;

        var pathext = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var found = FindOnPath(tool, [".exe", ".com", ".cmd", ".bat", ..pathext, ""]);
        if (found is null)
            return null;

        var ext = Path.GetExtension(found);
        // .cmd/.bat cannot be launched via CreateProcess; go through cmd.exe /c (ArgumentList, no shell string).
        // Note: for a spaced install path this relies on cmd's "quoted executable" rule — safe for our
        // space-free arguments (subscription ids, profile names, regions). If an argument ever needs spaces,
        // prefer resolving a native .exe launcher for that tool instead of the .cmd shim.
        if (ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            ext.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            var comspec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            return (comspec, ["/c", found]);
        }

        return (found, Array.Empty<string>());
    }

    private static string? FindOnPath(string tool, IReadOnlyList<string> extensions)
    {
        // If a rooted path was supplied, only check it (with extensions).
        if (Path.IsPathRooted(tool))
        {
            foreach (var ext in extensions)
            {
                var candidate = tool + ext;
                if (File.Exists(candidate))
                    return candidate;
            }
            return File.Exists(tool) ? tool : null;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var ext in extensions)
            {
                string candidate;
                try { candidate = Path.Combine(dir, tool + ext); }
                catch { continue; }
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }

    private sealed class CaptureProgress(StringBuilder stdout, StringBuilder stderr) : IProgress<ProcessOutputEvent>
    {
        public void Report(ProcessOutputEvent value)
        {
            var sb = value.Stream == OutputStream.Stdout ? stdout : stderr;
            lock (sb)
                sb.AppendLine(value.Text);
        }
    }
}
