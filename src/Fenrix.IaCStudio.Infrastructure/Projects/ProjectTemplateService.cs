using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// Serves the project-template catalog (Phase 12): built-in templates from <see cref="BuiltInTemplates"/> plus
/// user templates stored one-JSON-per-template under <c>&lt;dataRoot&gt;\Templates</c>. Applying a template writes
/// its files into each environment's working directory. No database involvement — templates are files/code.
/// See docs/32-project-templates.md.
/// </summary>
public sealed class ProjectTemplateService(
    IWorkspacePaths paths,
    ILogger<ProjectTemplateService> logger) : IProjectTemplateService
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<ProjectTemplateService> _logger = logger;

    private string TemplatesDir => Path.Combine(_paths.DataRoot, "Templates");

    public IReadOnlyList<ProjectTemplateInfo> List()
    {
        var all = new List<ProjectTemplateInfo>();
        all.AddRange(BuiltInTemplates.All.Select(t => t.Info));
        all.AddRange(LoadUserTemplates().Select(t => t.Info));
        return all
            .OrderByDescending(t => t.IsBuiltIn)
            .ThenBy(t => t.Provider)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public ProjectTemplate? Get(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var builtin = BuiltInTemplates.All.FirstOrDefault(t => t.Info.Id == id);
        if (builtin is not null) return builtin;
        return LoadUserTemplate(id);
    }

    public async Task ApplyAsync(string templateId, string projectRoot, IEnumerable<string> environmentSlugs, CancellationToken ct = default)
    {
        var template = Get(templateId);
        if (template is null)
        {
            _logger.LogWarning("Project template {Id} not found; leaving the blank scaffold in place.", templateId);
            return;
        }

        foreach (var slug in environmentSlugs)
        {
            var envDir = Path.Combine(projectRoot, "environments", slug);
            Directory.CreateDirectory(envDir);

            foreach (var file in template.Files)
            {
                // Convention: a template's terraform.tfvars becomes the environment's own <slug>.tfvars so it
                // loads through the environment's var-file (Fenrix's per-environment values model).
                var relative = file.RelativePath.Equals("terraform.tfvars", StringComparison.OrdinalIgnoreCase)
                    ? $"{slug}.tfvars"
                    : file.RelativePath;

                var target = Path.GetFullPath(Path.Combine(envDir, relative));
                // Path-escape guard: never write outside the environment directory.
                if (!target.StartsWith(Path.GetFullPath(envDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(target, Path.GetFullPath(envDir), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping template file {Path} (escapes the environment directory).", file.RelativePath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await File.WriteAllTextAsync(target, file.Content, Utf8NoBom, ct).ConfigureAwait(false);
            }
        }

        _logger.LogInformation("Applied template {Id} to {Count} environment(s) at {Root}.",
            templateId, environmentSlugs.Count(), projectRoot);
    }

    public async Task<ProjectTemplateInfo> SaveUserTemplateAsync(SaveTemplateRequest request, CancellationToken ct = default)
    {
        var id = string.IsNullOrWhiteSpace(request.Id) ? $"user-{Guid.NewGuid():N}" : request.Id!;
        if (BuiltInTemplates.All.Any(t => t.Info.Id == id))
            throw new InvalidOperationException("Cannot overwrite a built-in template; save under a new name.");

        var info = new ProjectTemplateInfo(
            id, request.Name.Trim(), request.Description?.Trim() ?? string.Empty,
            request.Provider, request.Category, request.CostTier, request.CostSummary ?? string.Empty,
            request.Tags ?? [], IsBuiltIn: false);

        var template = new ProjectTemplate(info, request.Files ?? []);

        Directory.CreateDirectory(TemplatesDir);
        var path = Path.Combine(TemplatesDir, id + ".json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(template, Json), Utf8NoBom, ct).ConfigureAwait(false);
        _logger.LogInformation("Saved user template {Id} ({Name}).", id, info.Name);
        return info;
    }

    public Task DeleteUserTemplateAsync(string id, CancellationToken ct = default)
    {
        if (BuiltInTemplates.All.Any(t => t.Info.Id == id))
            throw new InvalidOperationException("Built-in templates cannot be deleted.");

        var path = Path.Combine(TemplatesDir, id + ".json");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public async Task<ProjectTemplateInfo> CreateFromProjectAsync(
        string projectRootPath, string environmentWorkingDir, SaveTemplateRequest metadata, CancellationToken ct = default)
    {
        var dir = Path.IsPathRooted(environmentWorkingDir)
            ? environmentWorkingDir
            : Path.Combine(projectRootPath, environmentWorkingDir);

        var files = new List<ProjectTemplateFile>();
        if (Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext is not (".tf" or ".tfvars" or ".hcl" or ".md" or ".tftpl")) continue;
                var rel = Path.GetRelativePath(dir, path).Replace('\\', '/');
                var content = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                files.Add(new ProjectTemplateFile(rel, content));
            }
        }

        return await SaveUserTemplateAsync(metadata with { Files = files }, ct).ConfigureAwait(false);
    }

    // ── user-template file storage ─────────────────────────────────────────
    private IEnumerable<ProjectTemplate> LoadUserTemplates()
    {
        if (!Directory.Exists(TemplatesDir)) yield break;
        foreach (var path in Directory.EnumerateFiles(TemplatesDir, "*.json"))
        {
            var t = TryLoad(path);
            if (t is not null) yield return t;
        }
    }

    private ProjectTemplate? LoadUserTemplate(string id)
    {
        var path = Path.Combine(TemplatesDir, id + ".json");
        return File.Exists(path) ? TryLoad(path) : null;
    }

    private ProjectTemplate? TryLoad(string path)
    {
        try
        {
            var template = JsonSerializer.Deserialize<ProjectTemplate>(File.ReadAllText(path), Json);
            // Force IsBuiltIn=false regardless of what's on disk.
            if (template is not null && template.Info.IsBuiltIn)
                template = template with { Info = template.Info with { IsBuiltIn = false } };
            return template;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read user template {Path}.", path);
            return null;
        }
    }
}
