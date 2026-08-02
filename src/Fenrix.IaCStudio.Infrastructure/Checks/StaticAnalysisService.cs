using Fenrix.IaCStudio.Application.Abstractions.Checks;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Checks;
using Fenrix.IaCStudio.Contracts.Checks;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Runs the static-analysis tools over an environment's working directory and normalises their output into
/// findings. Standalone and read-only: no environment lock, no plan/apply involvement, output never logged.
/// TFLint provides lint/deprecations; the security scan uses Trivy (<c>trivy config</c>) when installed,
/// otherwise tfsec — running one avoids duplicate misconfiguration findings. See docs/34-checks.md.
/// </summary>
public sealed class StaticAnalysisService(
    IProjectService projects,
    ICheckToolDiscovery discovery,
    CheckProcessRunner runner,
    ILogger<StaticAnalysisService> logger) : IStaticAnalysisService
{
    private static readonly IReadOnlyDictionary<string, string> NoEnv = new Dictionary<string, string>(0);

    private readonly IProjectService _projects = projects;
    private readonly ICheckToolDiscovery _discovery = discovery;
    private readonly CheckProcessRunner _runner = runner;
    private readonly ILogger<StaticAnalysisService> _logger = logger;

    public async Task<StaticAnalysisReport> AnalyzeAsync(
        Guid projectId, Guid environmentId, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Project not found.");
        var env = project.Environments.FirstOrDefault(e => e.Id == environmentId)
            ?? throw new InvalidOperationException("Environment not found.");

        var workingDir = ResolveWorkingDir(project.RootPath, env.WorkingDirectory);
        if (!Directory.Exists(workingDir))
            return StaticAnalysisReport.Empty;

        var runs = new List<CheckToolRun>();

        // --- TFLint (lint / deprecations) ---
        var tflint = await _discovery.ResolveAsync(CheckTool.TfLint, projectId, ct).ConfigureAwait(false);
        if (tflint.Installed && tflint.ExecutablePath is not null)
        {
            progress?.Report("Running TFLint…");
            runs.Add(await RunToolAsync(
                CheckTool.TfLint, tflint.ExecutablePath, workingDir,
                ["--format", "json"], TfLintJsonParser.Parse, ct).ConfigureAwait(false));
        }
        else
        {
            runs.Add(CheckToolRun.NotAvailable(CheckTool.TfLint));
        }

        // --- Security scan: Trivy preferred, else tfsec ---
        var trivy = await _discovery.ResolveAsync(CheckTool.Trivy, projectId, ct).ConfigureAwait(false);
        var tfsec = await _discovery.ResolveAsync(CheckTool.Tfsec, projectId, ct).ConfigureAwait(false);

        if (trivy.Installed && trivy.ExecutablePath is not null)
        {
            progress?.Report("Running Trivy (config scan)…");
            runs.Add(await RunToolAsync(
                CheckTool.Trivy, trivy.ExecutablePath, workingDir,
                ["config", ".", "--format", "json", "--quiet"], TrivyJsonParser.Parse, ct).ConfigureAwait(false));
        }
        else if (tfsec.Installed && tfsec.ExecutablePath is not null)
        {
            progress?.Report("Running tfsec…");
            runs.Add(await RunToolAsync(
                CheckTool.Tfsec, tfsec.ExecutablePath, workingDir,
                [".", "--format", "json", "--no-colour"], TfsecJsonParser.Parse, ct).ConfigureAwait(false));
        }
        else
        {
            // No security scanner installed — surface a single "not installed" (Trivy is the recommended one).
            runs.Add(CheckToolRun.NotAvailable(CheckTool.Trivy));
        }

        var findings = runs
            .SelectMany(r => r.Findings)
            .OrderByDescending(f => (int)f.Severity)
            .ThenBy(f => f.Tool)
            .ThenBy(f => f.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line ?? int.MaxValue)
            .ToList();

        return new StaticAnalysisReport(runs, findings);
    }

    private async Task<CheckToolRun> RunToolAsync(
        CheckTool tool, string exe, string workingDir, IReadOnlyList<string> args,
        Func<string, IReadOnlyList<CheckFinding>> parse, CancellationToken ct)
    {
        try
        {
            var exec = await _runner.ExecuteAsync(
                exe, workingDir, args, NoEnv, $"{CheckToolMetadata.DisplayName(tool)} scan", ct).ConfigureAwait(false);

            if (exec.Process.Cancelled)
                return new CheckToolRun(tool, true, true, exec.Process.ExitCode, [], true, null);

            var findings = parse(exec.StandardOutput);

            // A non-zero exit with no parsed findings usually means the tool itself errored (bad config,
            // missing init). Surface a short, non-secret reason from stderr so the UI can guide the user.
            string? error = null;
            if (findings.Count == 0 && exec.Process.ExitCode != 0)
                error = FirstLine(exec.StandardError) ?? FirstLine(exec.StandardOutput)
                        ?? $"{CheckToolMetadata.DisplayName(tool)} exited with code {exec.Process.ExitCode}.";

            return new CheckToolRun(tool, true, true, exec.Process.ExitCode, findings, false, error);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Tool} run failed.", tool);
            return new CheckToolRun(tool, true, false, -1, [], false, ex.Message);
        }
    }

    private static string? FirstLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        foreach (var line in s.Split('\n'))
        {
            var t = line.Trim();
            if (t.Length > 0) return t;
        }
        return null;
    }

    private static string ResolveWorkingDir(string projectRoot, string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir)) return projectRoot;
        return Path.IsPathRooted(workingDir) ? workingDir : Path.Combine(projectRoot, workingDir);
    }
}
