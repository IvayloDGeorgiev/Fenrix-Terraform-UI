using Fenrix.IaCStudio.Application.Abstractions.Authoring;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Authoring;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Application.Hcl;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Persists visual-builder output to real <c>.tf</c> files. Generation is pure (ConfigHclBuilder / HclEmitter);
/// this service is the thin filesystem seam that routes every write through <see cref="IFileTreeService"/> so it
/// is atomic, journalled, and versioned for recovery — the same path the editor uses (ADR-0002). Round-trip
/// edits are applied as in-place value-span splices, preserving all unsupported HCL. See docs/07-visual-builder.md.
/// </summary>
public sealed class ConfigAuthoringService(
    IProjectService projects,
    IFileTreeService files,
    ILogger<ConfigAuthoringService> logger) : IConfigAuthoringService
{
    private readonly IProjectService _projects = projects;
    private readonly IFileTreeService _files = files;
    private readonly ILogger<ConfigAuthoringService> _logger = logger;

    public async Task<IReadOnlyList<string>> ListConfigFilesAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return [];

        var environment = project.Environments.FirstOrDefault(e => e.Id == environmentId);
        var workingDir = TerraformIntegrity.ResolveWorkingDirectory(project, environment);
        if (!Directory.Exists(workingDir))
            return [];

        try
        {
            return Directory.EnumerateFiles(workingDir, "*.tf", SearchOption.TopDirectoryOnly)
                .Select(f => FileTrackingPolicy.ToRelative(project.RootPath, f))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not list config files for {Project}.", projectId);
            return [];
        }
    }

    public async Task<AuthoringWriteResult> AppendAsync(Guid projectId, string relativePath, string hcl, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return AuthoringWriteResult.Fail(relativePath, "Project not found.");

        try
        {
            var full = ResolveInside(project.RootPath, relativePath);
            var existing = File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : string.Empty;

            var content = Combine(existing, hcl);
            await _files.WriteFileAsync(projectId, project.RootPath, relativePath, content, ct);
            return AuthoringWriteResult.Ok(relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not append generated HCL to {Path}.", relativePath);
            return AuthoringWriteResult.Fail(relativePath, ex.Message);
        }
    }

    public async Task<AuthoringFile?> ReadFileAsync(Guid projectId, string relativePath, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return null;

        var full = ResolveInside(project.RootPath, relativePath);
        if (!File.Exists(full))
            return null;

        var content = await File.ReadAllTextAsync(full, ct);
        var blocks = HclReader.ReadOutline(content);
        return new AuthoringFile(relativePath, content, blocks);
    }

    public async Task<AuthoringWriteResult> ApplyLiteralEditsAsync(
        Guid projectId, string relativePath, IReadOnlyList<LiteralEdit> edits, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return AuthoringWriteResult.Fail(relativePath, "Project not found.");

        try
        {
            var full = ResolveInside(project.RootPath, relativePath);
            if (!File.Exists(full))
                return AuthoringWriteResult.Fail(relativePath, "File not found.");

            var content = await File.ReadAllTextAsync(full, ct);

            // Apply from the end backwards so earlier offsets stay valid. Overlapping edits are rejected.
            var ordered = edits.OrderByDescending(e => e.ValueStart).ToList();
            var lastStart = int.MaxValue;
            foreach (var edit in ordered)
            {
                if (edit.ValueStart < 0 || edit.ValueEnd > content.Length || edit.ValueStart > edit.ValueEnd)
                    return AuthoringWriteResult.Fail(relativePath, "An edit span is out of range.");
                if (edit.ValueEnd > lastStart)
                    return AuthoringWriteResult.Fail(relativePath, "Overlapping edits are not allowed.");
                content = content[..edit.ValueStart] + edit.NewValueText + content[edit.ValueEnd..];
                lastStart = edit.ValueStart;
            }

            await _files.WriteFileAsync(projectId, project.RootPath, relativePath, content, ct);
            return AuthoringWriteResult.Ok(relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not apply literal edits to {Path}.", relativePath);
            return AuthoringWriteResult.Fail(relativePath, ex.Message);
        }
    }

    /// <summary>Appends a block to existing content, ensuring exactly one blank line between them.</summary>
    private static string Combine(string existing, string hcl)
    {
        var trimmedNew = hcl.TrimEnd('\n') + "\n";
        if (string.IsNullOrWhiteSpace(existing))
            return trimmedNew;
        var baseText = existing.TrimEnd('\n');
        return baseText + "\n\n" + trimmedNew;
    }

    /// <summary>Resolves a project-relative path to an absolute path, refusing to escape the project root.</summary>
    private static string ResolveInside(string projectRoot, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        var rootFull = Path.GetFullPath(projectRoot);
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path '{relativePath}' escapes the project root.");
        return full;
    }
}
