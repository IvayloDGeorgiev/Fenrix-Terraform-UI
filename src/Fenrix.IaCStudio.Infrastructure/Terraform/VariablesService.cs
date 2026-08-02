using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Infrastructure.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Per-environment variables manager (Phase 12). Parses <c>variable</c> declarations from an environment's
/// <c>.tf</c> files and merges them with the environment's tfvars values (via <see cref="VariableParser"/>),
/// then rewrites the tfvars file through the atomic-write + file-history path. See docs/33-variables.md.
/// </summary>
public sealed class VariablesService(
    IProjectService projects,
    IFileTreeService files,
    ILogger<VariablesService> logger) : IVariablesService
{
    private readonly IProjectService _projects = projects;
    private readonly IFileTreeService _files = files;
    private readonly ILogger<VariablesService> _logger = logger;

    public async Task<EnvironmentVariables> LoadAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var (project, env) = await ResolveAsync(projectId, environmentId, ct);
        var workingDir = ResolveWorkingDir(project.RootPath, env.WorkingDirectory);
        var tfvarsName = string.IsNullOrWhiteSpace(env.VariablesFile)
            ? $"{ProjectScaffolder.Slug(env.Name)}.tfvars"
            : env.VariablesFile!;

        var declarations = new List<VariableParser.Declaration>();
        if (Directory.Exists(workingDir))
        {
            foreach (var tf in Directory.EnumerateFiles(workingDir, "*.tf", SearchOption.TopDirectoryOnly))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var content = await File.ReadAllTextAsync(tf, ct).ConfigureAwait(false);
                    declarations.AddRange(VariableParser.ParseDeclarations(content));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not parse variables from {File}", tf);
                }
            }
        }

        var tfvarsPath = Path.Combine(workingDir, tfvarsName);
        var tfvars = File.Exists(tfvarsPath)
            ? VariableParser.ParseTfvars(await File.ReadAllTextAsync(tfvarsPath, ct).ConfigureAwait(false))
            : new Dictionary<string, string>();

        var merged = VariableParser.Merge(declarations, tfvars);
        return new EnvironmentVariables(tfvarsName, merged);
    }

    public async Task SaveAsync(Guid projectId, Guid environmentId, IReadOnlyList<VariableValueEdit> edits, CancellationToken ct = default)
    {
        var (project, env) = await ResolveAsync(projectId, environmentId, ct);
        var tfvarsName = string.IsNullOrWhiteSpace(env.VariablesFile)
            ? $"{ProjectScaffolder.Slug(env.Name)}.tfvars"
            : env.VariablesFile!;
        var relativePath = CombineRelative(env.WorkingDirectory, tfvarsName);

        var sb = new StringBuilder();
        sb.Append("# Variable values for the ").Append(env.Name).Append(" environment. Managed by Fenrix.\n\n");
        foreach (var edit in edits)
        {
            if (string.IsNullOrWhiteSpace(edit.Raw)) continue; // unset ⇒ omit
            sb.Append(edit.Name).Append(" = ").Append(edit.Raw!.Trim()).Append('\n');
        }

        await _files.WriteFileAsync(projectId, project.RootPath, relativePath, sb.ToString(), ct).ConfigureAwait(false);
        _logger.LogInformation("Saved {Count} variable value(s) to {File}", edits.Count(e => !string.IsNullOrWhiteSpace(e.Raw)), relativePath);
    }

    private async Task<(Domain.Projects.InfrastructureProject Project, Domain.Environments.ProjectEnvironment Env)> ResolveAsync(
        Guid projectId, Guid environmentId, CancellationToken ct)
    {
        var project = await _projects.GetAsync(projectId, ct)
            ?? throw new InvalidOperationException("Project not found.");
        var env = project.Environments.FirstOrDefault(e => e.Id == environmentId)
            ?? throw new InvalidOperationException("Environment not found.");
        return (project, env);
    }

    private static string ResolveWorkingDir(string projectRoot, string? workingDir)
    {
        if (string.IsNullOrWhiteSpace(workingDir)) return projectRoot;
        return Path.IsPathRooted(workingDir) ? workingDir : Path.Combine(projectRoot, workingDir);
    }

    private static string CombineRelative(string? workingDir, string fileName)
        => string.IsNullOrWhiteSpace(workingDir) ? fileName : $"{workingDir!.Replace('\\', '/').TrimEnd('/')}/{fileName}";
}
