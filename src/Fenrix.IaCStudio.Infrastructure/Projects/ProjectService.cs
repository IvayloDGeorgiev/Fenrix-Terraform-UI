using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Contracts.Files;
using Fenrix.IaCStudio.Contracts.Projects;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Files;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// Registers and retrieves projects, orchestrating on-disk scaffolding, the manifest, and a baseline
/// history capture. Files remain the source of truth; nothing is moved on import. See docs/03-domain-model.md.
/// </summary>
public sealed class ProjectService(
    AppDbContext db,
    IProjectScaffolder scaffolder,
    IProjectManifestStore manifestStore,
    IFileHistoryStore history,
    IGitRepositoryInitializer gitInitializer,
    IProjectTemplateService templates,
    IWorkspacePaths paths,
    ILogger<ProjectService> logger) : IProjectService
{
    private readonly AppDbContext _db = db;
    private readonly IProjectScaffolder _scaffolder = scaffolder;
    private readonly IProjectManifestStore _manifestStore = manifestStore;
    private readonly IFileHistoryStore _history = history;
    private readonly IGitRepositoryInitializer _gitInitializer = gitInitializer;
    private readonly IProjectTemplateService _templates = templates;
    private readonly IWorkspacePaths _paths = paths;
    private readonly ILogger<ProjectService> _logger = logger;

    public async Task<InfrastructureProject> CreateAsync(CreateProjectRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("A project name is required.", nameof(request));

        var parent = string.IsNullOrWhiteSpace(request.ParentDirectory)
            ? _paths.ProjectsDirectory
            : request.ParentDirectory!;
        var projectRoot = UniqueDirectory(Path.Combine(parent, SafeFolderName(request.Name)));

        var environments = request.Environments.Count > 0
            ? request.Environments
            : CreateProjectRequest.DefaultEnvironments();
        request.Environments = environments;

        await _scaffolder.ScaffoldAsync(projectRoot, request, ct);

        // If a project template was chosen, prefill every environment's working directory with its files
        // (overwriting the blank placeholders). Non-fatal — a template problem must not fail project creation.
        if (!string.IsNullOrWhiteSpace(request.TemplateId))
        {
            try
            {
                var slugs = environments.Select(e => ProjectScaffolder.Slug(e.Name));
                await _templates.ApplyAsync(request.TemplateId!, projectRoot, slugs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Applying template {Template} failed; project created with the blank scaffold.", request.TemplateId);
            }
        }

        // Initialise a Git repository for the new project (docs/08-git-engine.md). Non-fatal: if Git is
        // unavailable the project is still created and can be initialised later from Source control.
        string? repositoryRoot = null;
        if (request.InitializeGit && await _gitInitializer.InitializeAsync(projectRoot, ct))
            repositoryRoot = projectRoot;

        var project = new InfrastructureProject
        {
            Name = request.Name.Trim(),
            RootPath = projectRoot,
            RepositoryRootPath = repositoryRoot,
            Description = request.Description,
            RequiredTerraformVersion = request.RequiredTerraformVersion,
            IsLinked = !IsUnderProjectsDirectory(projectRoot),
            Environments = environments.Select((e, i) => new ProjectEnvironment
            {
                Name = e.Name,
                WorkingDirectory = $"environments/{ProjectScaffolder.Slug(e.Name)}",
                VariablesFile = $"{ProjectScaffolder.Slug(e.Name)}.tfvars",
                BackendConfigFile = "backend.hcl",
                CloudConnectionId = e.CloudConnectionId,
                IsProduction = e.IsProduction,
                DisplayOrder = i
            }).ToList()
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        if (request.WriteManifest)
            await _manifestStore.WriteAsync(projectRoot, ToManifest(project), ct);

        await CaptureBaselineAsync(project, ct);

        _logger.LogInformation("Created project {Name} ({Id}) at {Root}", project.Name, project.Id, projectRoot);
        return project;
    }

    public async Task<InfrastructureProject> ImportAsync(ImportScanResult scan, CancellationToken ct = default)
    {
        if (!Directory.Exists(scan.RootPath))
            throw new DirectoryNotFoundException($"'{scan.RootPath}' does not exist.");

        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(scan.RootPath));

        var project = new InfrastructureProject
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Imported project" : name,
            RootPath = Path.GetFullPath(scan.RootPath),
            RepositoryRootPath = scan.RepositoryRootPath,
            RequiredTerraformVersion = scan.DetectedTerraformVersion,
            IsLinked = !IsUnderProjectsDirectory(scan.RootPath),
            Environments = scan.SuggestedEnvironments
                .Where(e => e.Include)
                .Select((e, i) => new ProjectEnvironment
                {
                    Name = e.Name,
                    WorkingDirectory = e.RelativePath,
                    VariablesFile = e.VariablesFile,
                    BackendConfigFile = e.BackendConfigFile,
                    TerraformWorkspace = e.TerraformWorkspace,
                    IsProduction = e.IsProduction,
                    DisplayOrder = i
                }).ToList()
        };

        _db.Projects.Add(project);
        await _db.SaveChangesAsync(ct);

        // Non-destructive: no manifest is written on import unless the user asks later.
        await CaptureBaselineAsync(project, ct);

        _logger.LogInformation("Imported project {Name} ({Id}) from {Root} (linked={Linked})",
            project.Name, project.Id, project.RootPath, project.IsLinked);
        return project;
    }

    public async Task<IReadOnlyList<ProjectSummary>> ListAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        var query = _db.Projects.AsNoTracking();
        if (!includeArchived)
            query = query.Where(p => !p.IsArchived);

        return await query
            .OrderByDescending(p => p.LastOpenedAt ?? p.CreatedAt)
            .Select(p => new ProjectSummary
            {
                Id = p.Id,
                Name = p.Name,
                RootPath = p.RootPath,
                Description = p.Description,
                IsLinked = p.IsLinked,
                IsArchived = p.IsArchived,
                EnvironmentCount = p.Environments.Count,
                CreatedAt = p.CreatedAt,
                LastOpenedAt = p.LastOpenedAt
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProjectSummary>> GetRecentAsync(int take = 8, CancellationToken ct = default)
    {
        return await _db.Projects.AsNoTracking()
            .Where(p => !p.IsArchived && p.LastOpenedAt != null)
            .OrderByDescending(p => p.LastOpenedAt)
            .Take(take)
            .Select(p => new ProjectSummary
            {
                Id = p.Id,
                Name = p.Name,
                RootPath = p.RootPath,
                Description = p.Description,
                IsLinked = p.IsLinked,
                IsArchived = p.IsArchived,
                EnvironmentCount = p.Environments.Count,
                CreatedAt = p.CreatedAt,
                LastOpenedAt = p.LastOpenedAt
            })
            .ToListAsync(ct);
    }

    public Task<InfrastructureProject?> GetAsync(Guid projectId, CancellationToken ct = default)
        => _db.Projects
            .Include(p => p.Environments.OrderBy(e => e.DisplayOrder))
            .FirstOrDefaultAsync(p => p.Id == projectId, ct);

    public async Task TouchAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;
        project.LastOpenedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetArchivedAsync(Guid projectId, bool archived, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;
        project.IsArchived = archived;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetRepositoryConnectionAsync(Guid projectId, Guid? repositoryConnectionId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;
        project.RepositoryConnectionId = repositoryConnectionId;
        await _db.SaveChangesAsync(ct);
    }

    public async Task SetEnvironmentCloudConnectionAsync(
        Guid projectId, Guid environmentId, Guid? cloudConnectionId, CancellationToken ct = default)
    {
        var environment = await _db.Environments
            .FirstOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct);
        if (environment is null)
            return;
        environment.CloudConnectionId = cloudConnectionId;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Bound environment {Env} to cloud connection {Conn}", environmentId, cloudConnectionId);
    }

    public async Task SetEnvironmentWorkspaceAsync(
        Guid projectId, Guid environmentId, string? workspace, CancellationToken ct = default)
    {
        var environment = await _db.Environments
            .FirstOrDefaultAsync(e => e.Id == environmentId && e.ProjectId == projectId, ct);
        if (environment is null)
            return;
        environment.TerraformWorkspace = string.IsNullOrWhiteSpace(workspace) ? null : workspace.Trim();
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Set environment {Env} Terraform workspace to {Workspace}", environmentId, environment.TerraformWorkspace);
    }

    public async Task RemoveAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId, ct);
        if (project is null)
            return;
        // Unregister only — never touch files on disk.
        _db.Projects.Remove(project);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Unregistered project {Id} (files left on disk)", projectId);
    }

    // ---- helpers ----

    private async Task CaptureBaselineAsync(InfrastructureProject project, CancellationToken ct)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(project.RootPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Baseline capture skipped for {Root}", project.RootPath);
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = FileTrackingPolicy.ToRelative(project.RootPath, file);
            if (!FileTrackingPolicy.IsVersioned(rel))
                continue;

            await _history.RecordAsync(new FileChange
            {
                ProjectId = project.Id,
                RelativePath = rel,
                FullPath = file,
                ChangeKind = FileChangeKind.Created,
                Origin = ChangeOrigin.Import
            }, ct);
        }
    }

    private static ProjectManifest ToManifest(InfrastructureProject project) => new()
    {
        SchemaVersion = 1,
        ProjectId = project.Id,
        Name = project.Name,
        TerraformVersion = project.RequiredTerraformVersion,
        Environments = project.Environments
            .OrderBy(e => e.DisplayOrder)
            .Select(e => new ManifestEnvironment
            {
                Name = e.Name,
                Path = e.WorkingDirectory,
                VariablesFile = e.VariablesFile,
                BackendConfigFile = e.BackendConfigFile,
                IsProduction = e.IsProduction
            }).ToList()
    };

    private bool IsUnderProjectsDirectory(string path)
    {
        var root = Path.GetFullPath(_paths.ProjectsDirectory);
        var full = Path.GetFullPath(path);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    private static string UniqueDirectory(string desired)
    {
        if (!Directory.Exists(desired) || IsEmpty(desired))
            return desired;

        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{desired}-{i}";
            if (!Directory.Exists(candidate) || IsEmpty(candidate))
                return candidate;
        }
        throw new IOException($"Could not find a free directory name near '{desired}'.");

        static bool IsEmpty(string dir) => !Directory.EnumerateFileSystemEntries(dir).Any();
    }
}
