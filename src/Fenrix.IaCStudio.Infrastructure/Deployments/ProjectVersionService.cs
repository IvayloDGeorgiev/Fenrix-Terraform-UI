using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Deployments;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Domain.Versioning;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Terraform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Deployments;

/// <summary>
/// Manages per-project, Git-anchored versions. "Cut a version" snapshots the current HEAD (commit + branch +
/// config/provider-lock hashes) and optionally pushes an annotated tag; versions can also be inferred from
/// tags already in the repository. Never copies files — a version is a Git ref plus metadata (ADR-0002).
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class ProjectVersionService(
    AppDbContext db,
    IProjectService projects,
    IGitService git,
    IUserContext userContext,
    ILogger<ProjectVersionService> logger) : IProjectVersionService
{
    private readonly AppDbContext _db = db;
    private readonly IProjectService _projects = projects;
    private readonly IGitService _git = git;
    private readonly IUserContext _userContext = userContext;
    private readonly ILogger<ProjectVersionService> _logger = logger;

    public async Task<IReadOnlyList<ProjectVersionSummary>> ListAsync(Guid projectId, CancellationToken ct = default)
    {
        var rows = await _db.ProjectVersions.AsNoTracking()
            .Where(v => v.ProjectId == projectId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ProjectVersionSummary?> GetAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await _db.ProjectVersions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == versionId, ct);
        return v is null ? null : Map(v);
    }

    public async Task<CutVersionResult> CutAsync(CutVersionRequest request, CancellationToken ct = default)
    {
        var label = request.Label?.Trim() ?? string.Empty;
        if (label.Length == 0)
            return CutVersionResult.Fail("A version label is required.");

        var project = await _projects.GetAsync(request.ProjectId, ct);
        if (project is null)
            return CutVersionResult.Fail("Project not found.");

        GitProvenance prov;
        try
        {
            prov = await _git.ReadProvenanceAsync(project.RepositoryRootPath ?? project.RootPath, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read Git provenance while cutting a version for {Project}.", project.Name);
            return CutVersionResult.Fail("Could not read the Git repository state.");
        }

        if (!prov.IsRepository || string.IsNullOrEmpty(prov.CommitSha))
            return CutVersionResult.Fail("Cutting a version requires a Git commit. Initialise the repository and commit first.");

        // Duplicate-label guard (per project).
        var exists = await _db.ProjectVersions.AnyAsync(
            v => v.ProjectId == project.Id && v.Label == label, ct);
        if (exists)
            return CutVersionResult.Fail($"A version labelled '{label}' already exists for this project.");

        // Project-level config / provider-lock snapshot (parity with SavedPlan integrity hashes).
        string configHash = string.Empty, lockHash = string.Empty;
        try
        {
            configHash = await TerraformIntegrity.ComputeConfigHashAsync(project.RootPath, project.RootPath, ct);
            lockHash = await TerraformIntegrity.ComputeLockHashAsync(project.RootPath, ct) ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not compute config/lock hash while cutting version {Label}.", label);
        }

        var version = new ProjectVersion
        {
            ProjectId = project.Id,
            Label = label,
            GitCommit = prov.CommitSha!,
            GitBranch = prov.Branch,
            ConfigurationHash = configHash,
            ProviderLockHash = lockHash,
            RequiredTerraformVersion = project.RequiredTerraformVersion,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes!.Trim(),
            CreatedBy = _userContext.Current.DisplayName
        };

        string? warning = prov.IsDirty
            ? "The working tree has uncommitted changes; the version anchors to the last commit, not those changes."
            : null;

        // Optional annotated Git tag at the version commit, matching the label. Record the tag on the version
        // only when it was actually created.
        if (request.CreateGitTag)
        {
            var tag = await _git.CreateTagAsync(project.Id,
                new GitTagRequest(label, Annotated: true, Message: $"Fenrix version {label}", Target: prov.CommitSha), ct);
            if (tag.Succeeded)
            {
                version = RebuildWithTag(version, label);
                if (request.PushGitTag)
                {
                    var push = await _git.PushTagAsync(project.Id, label, null, null, ct);
                    if (!push.Succeeded)
                        warning = Combine(warning, $"Tag created locally, but the push failed: {Reason(push)}");
                }
            }
            else
            {
                warning = Combine(warning, $"Version saved, but the Git tag could not be created: {Reason(tag)}");
            }
        }

        _db.ProjectVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Cut version {Label} for {Project} at {Commit}.", label, project.Name, Short(version.GitCommit));
        return new CutVersionResult(true, Map(version), warning, null);
    }

    public async Task<IReadOnlyList<ProjectVersionSummary>> InferFromTagsAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null) return [];

        IReadOnlyList<GitTag> tags;
        try { tags = await _git.GetTagsAsync(projectId, ct); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not list tags for {Project}.", project.Name); return []; }

        var existing = await _db.ProjectVersions
            .Where(v => v.ProjectId == projectId)
            .Select(v => new { v.Label, v.GitTag, v.GitCommit })
            .ToListAsync(ct);
        var takenLabels = existing.Select(e => e.Label).ToHashSet(StringComparer.Ordinal);
        var takenTags = existing.Where(e => e.GitTag is not null).Select(e => e.GitTag!).ToHashSet(StringComparer.Ordinal);

        var created = new List<ProjectVersion>();
        foreach (var tag in tags)
        {
            if (takenTags.Contains(tag.Name) || takenLabels.Contains(tag.Name))
                continue;

            var v = new ProjectVersion
            {
                ProjectId = projectId,
                Label = tag.Name,
                GitCommit = tag.TargetSha,
                GitTag = tag.Name,
                GitBranch = null,
                ConfigurationHash = string.Empty,
                ProviderLockHash = string.Empty,
                RequiredTerraformVersion = project.RequiredTerraformVersion,
                Notes = tag.Subject,
                CreatedAt = tag.Date == default ? DateTimeOffset.UtcNow : tag.Date,
                CreatedBy = "git"
            };
            created.Add(v);
            takenLabels.Add(tag.Name);
            takenTags.Add(tag.Name);
        }

        if (created.Count > 0)
        {
            _db.ProjectVersions.AddRange(created);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Inferred {Count} version(s) from Git tags for {Project}.", created.Count, project.Name);
        }

        return created.Select(Map).ToList();
    }

    public async Task<ProjectVersionSummary?> UpdateAsync(Guid versionId, string label, string? notes, CancellationToken ct = default)
    {
        var v = await _db.ProjectVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return null;

        var trimmed = label?.Trim() ?? v.Label;
        if (trimmed.Length == 0) trimmed = v.Label;

        // Label + notes are the only editable metadata; the Git anchor is immutable.
        var replacement = new ProjectVersion
        {
            Id = v.Id,
            ProjectId = v.ProjectId,
            Label = trimmed,
            GitCommit = v.GitCommit,
            GitTag = v.GitTag,
            GitBranch = v.GitBranch,
            ConfigurationHash = v.ConfigurationHash,
            ProviderLockHash = v.ProviderLockHash,
            RequiredTerraformVersion = v.RequiredTerraformVersion,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes!.Trim(),
            CreatedAt = v.CreatedAt,
            CreatedBy = v.CreatedBy
        };
        _db.Entry(v).CurrentValues.SetValues(replacement);
        await _db.SaveChangesAsync(ct);
        return Map(replacement);
    }

    public async Task<bool> DeleteAsync(Guid versionId, CancellationToken ct = default)
    {
        var v = await _db.ProjectVersions.FirstOrDefaultAsync(x => x.Id == versionId, ct);
        if (v is null) return false;

        var referenced = await _db.Deployments.AnyAsync(d => d.ProjectVersionId == versionId, ct);
        if (referenced)
            return false; // keep history intact

        _db.ProjectVersions.Remove(v);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    // ---- helpers ----

    private static ProjectVersion RebuildWithTag(ProjectVersion v, string tag) => new()
    {
        Id = v.Id,
        ProjectId = v.ProjectId,
        Label = v.Label,
        GitCommit = v.GitCommit,
        GitTag = tag,
        GitBranch = v.GitBranch,
        ConfigurationHash = v.ConfigurationHash,
        ProviderLockHash = v.ProviderLockHash,
        RequiredTerraformVersion = v.RequiredTerraformVersion,
        Notes = v.Notes,
        CreatedAt = v.CreatedAt,
        CreatedBy = v.CreatedBy
    };

    internal static ProjectVersionSummary Map(ProjectVersion v) => new(
        v.Id, v.ProjectId, v.Label, v.GitCommit, Short(v.GitCommit), v.GitTag, v.GitBranch,
        v.RequiredTerraformVersion, v.Notes, v.CreatedAt, v.CreatedBy);

    private static string Short(string sha) => string.IsNullOrEmpty(sha) ? "" : sha.Length > 7 ? sha[..7] : sha;

    private static string Combine(string? a, string b) => string.IsNullOrEmpty(a) ? b : $"{a} {b}";

    private static string Reason(GitOperationResult r) =>
        !string.IsNullOrWhiteSpace(r.Error) ? r.Error!
        : !string.IsNullOrWhiteSpace(r.Output) ? r.Output
        : "unknown error";
}
