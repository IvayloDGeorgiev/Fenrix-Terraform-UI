using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Orchestrates a typed Terraform command: resolve context, enforce the project's version constraint,
/// build the exact request + redacted preview, stream output through the runner, capture a raw log, and
/// record redacted history. Preview and execution share one argument list (see
/// <see cref="CommandPreviewBuilder"/>), so what the user sees is what runs. See
/// docs/05-terraform-engine.md, docs/23-command-transparency.md, docs/25-execution-lifecycle.md.
/// </summary>
public sealed class TerraformExecutor(
    IProjectService projects,
    ITerraformDiscovery discovery,
    IProcessRunner runner,
    ICommandHistoryStore history,
    IWorkspacePaths paths,
    ILogger<TerraformExecutor> logger) : ITerraformExecutor
{
    private const string Tool = "terraform";

    private readonly IProjectService _projects = projects;
    private readonly ITerraformDiscovery _discovery = discovery;
    private readonly IProcessRunner _runner = runner;
    private readonly ICommandHistoryStore _history = history;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<TerraformExecutor> _logger = logger;

    public async Task<TerraformRunPlan> PlanAsync(TerraformRunSpec spec, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(spec.ProjectId, ct);
        var environment = project?.Environments.FirstOrDefault(e => e.Id == spec.EnvironmentId);

        var workingDir = ResolveWorkingDirectory(project, environment);
        var installation = await _discovery.ResolveAsync(spec.ProjectId, ct);

        var executablePath = installation?.ExecutablePath ?? Tool;
        var request = CommandPreviewBuilder.BuildRequest(spec, executablePath, workingDir);

        var chips = BuildChips(installation, request.RiskLevel, project?.RequiredTerraformVersion);
        var preview = CommandPreviewBuilder.BuildPreview(request, chips);

        var blockReason = DetermineBlockReason(project, environment, workingDir, installation);
        return new TerraformRunPlan(request, preview, installation, blockReason);
    }

    public async Task<TerraformRunResult> ExecuteAsync(
        TerraformRunPlan plan,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default)
    {
        if (!plan.CanRun)
            throw new InvalidOperationException(plan.BlockReason ?? "This command cannot be run.");

        var request = plan.Request;
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

        var logPath = await WriteLogAsync(run.Id, full.ToString(), ct);

        var status = result.Cancelled
            ? TerraformRunStatus.Cancelled
            : result.ExitCode == 0 ? TerraformRunStatus.Succeeded : TerraformRunStatus.Failed;
        await _history.RecordCompletionAsync(run.Id, status.ToString(), result.ExitCode, result.CompletedAt, logPath, ct);

        var validation = request.Kind == TerraformCommandKind.Validate
            ? ParseValidation(stdout.ToString(), result.ExitCode)
            : null;

        return new TerraformRunResult(run.Id, result, logPath, validation);
    }

    private static string ResolveWorkingDirectory(InfrastructureProject? project, ProjectEnvironment? environment)
    {
        if (project is null)
            return string.Empty;
        var wd = environment?.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(wd))
            return project.RootPath;
        return Path.IsPathRooted(wd) ? wd : Path.Combine(project.RootPath, wd);
    }

    private static string? DetermineBlockReason(
        InfrastructureProject? project,
        ProjectEnvironment? environment,
        string workingDir,
        TerraformInstallation? installation)
    {
        if (project is null)
            return "Project not found.";
        if (environment is null)
            return "Select an environment to run against.";
        if (string.IsNullOrWhiteSpace(workingDir) || !Directory.Exists(workingDir))
            return $"Working directory not found: {workingDir}";
        if (installation is null)
            return "No Terraform binary found. Set the executable in Settings or install Terraform on your PATH.";
        if (installation.Version is null)
            return $"Could not read the version of the Terraform binary at {installation.ExecutablePath}.";
        if (!installation.SatisfiesConstraint(project.RequiredTerraformVersion))
            return $"Terraform {installation.Version} does not satisfy this project's required version '{project.RequiredTerraformVersion}'.";
        return null;
    }

    private static List<CommandContextChip> BuildChips(TerraformInstallation? installation, TerraformRiskLevel risk, string? requiredVersion)
    {
        var chips = new List<CommandContextChip>();
        chips.Add(new CommandContextChip("Terraform", installation?.Version?.ToString() ?? "not found"));
        if (!string.IsNullOrWhiteSpace(requiredVersion))
            chips.Add(new CommandContextChip("Requires", requiredVersion));
        chips.Add(new CommandContextChip("Risk", RiskLabel(risk)));
        return chips;
    }

    private static string RiskLabel(TerraformRiskLevel risk) => risk switch
    {
        TerraformRiskLevel.ReadOnly => "read-only",
        TerraformRiskLevel.Safe => "safe",
        TerraformRiskLevel.StateChanging => "state-changing",
        TerraformRiskLevel.Destructive => "destructive",
        _ => risk.ToString()
    };

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

    /// <summary>Parses <c>terraform validate -json</c> output, falling back to exit-code truthiness.</summary>
    internal static TerraformValidationResult ParseValidation(string stdout, int exitCode)
    {
        var trimmed = stdout?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return TerraformValidationResult.Unparsed(exitCode == 0);

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;

            var valid = root.TryGetProperty("valid", out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean()
                : exitCode == 0;
            var errorCount = root.TryGetProperty("error_count", out var ec) && ec.TryGetInt32(out var e) ? e : 0;
            var warningCount = root.TryGetProperty("warning_count", out var wc) && wc.TryGetInt32(out var w) ? w : 0;

            var diagnostics = new List<ValidationDiagnostic>();
            if (root.TryGetProperty("diagnostics", out var diags) && diags.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in diags.EnumerateArray())
                    diagnostics.Add(ParseDiagnostic(d));
            }

            return new TerraformValidationResult(valid, errorCount, warningCount, diagnostics, ParsedFromJson: true);
        }
        catch (JsonException)
        {
            return TerraformValidationResult.Unparsed(exitCode == 0);
        }
    }

    private static ValidationDiagnostic ParseDiagnostic(JsonElement d)
    {
        var severity = d.TryGetProperty("severity", out var s) && s.GetString() == "warning"
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        var summary = d.TryGetProperty("summary", out var sm) ? sm.GetString() ?? string.Empty : string.Empty;
        var detail = d.TryGetProperty("detail", out var dt) ? dt.GetString() : null;

        string? fileName = null;
        int? line = null;
        if (d.TryGetProperty("range", out var range) && range.ValueKind == JsonValueKind.Object)
        {
            if (range.TryGetProperty("filename", out var fn))
                fileName = fn.GetString();
            if (range.TryGetProperty("start", out var start) && start.TryGetProperty("line", out var ln) && ln.TryGetInt32(out var l))
                line = l;
        }

        return new ValidationDiagnostic(severity, summary, detail, fileName, line);
    }

    /// <summary>Forwards each output line to the UI while capturing the full log and stdout separately.</summary>
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
