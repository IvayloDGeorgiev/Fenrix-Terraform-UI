using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Deployments;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Versioning;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Deployments;

/// <summary>
/// CRUD for a project's optional pipeline definition (ordered environment stages + per-stage gates). One
/// pipeline per project this phase. A project without a pipeline still deploys per-environment; the pipeline
/// only adds gates to the governed deploy flow. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed class PipelineService(
    AppDbContext db,
    IProjectService projects,
    ILogger<PipelineService> logger) : IPipelineService
{
    private readonly AppDbContext _db = db;
    private readonly IProjectService _projects = projects;
    private readonly ILogger<PipelineService> _logger = logger;

    public async Task<PipelineDefinition?> GetAsync(Guid projectId, CancellationToken ct = default)
    {
        var pipeline = await _db.DeploymentPipelines
            .Include(p => p.Stages)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (pipeline is null) return null;

        var project = await _projects.GetAsync(projectId, ct);
        var envs = project?.Environments.ToDictionary(e => e.Id) ?? [];
        return Map(pipeline, envs);
    }

    public async Task<PipelineDefinition> SaveAsync(SavePipelineRequest request, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(request.ProjectId, ct)
            ?? throw new InvalidOperationException("Project not found.");
        var envs = project.Environments.ToDictionary(e => e.Id);

        // Replace any existing pipeline (one per project this phase).
        var existing = await _db.DeploymentPipelines
            .Include(p => p.Stages)
            .FirstOrDefaultAsync(p => p.ProjectId == request.ProjectId, ct);
        if (existing is not null)
        {
            _db.PipelineStages.RemoveRange(existing.Stages);
            _db.DeploymentPipelines.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        var pipeline = new DeploymentPipeline
        {
            ProjectId = request.ProjectId,
            Name = string.IsNullOrWhiteSpace(request.Name) ? "Release pipeline" : request.Name.Trim(),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var order = 0;
        foreach (var s in request.Stages.Where(s => envs.ContainsKey(s.EnvironmentId)))
        {
            pipeline.Stages.Add(new PipelineStage
            {
                PipelineId = pipeline.Id,
                EnvironmentId = s.EnvironmentId,
                Order = order++,
                RequireApproval = s.RequireApproval,
                RequirePreviousStageSuccess = s.RequirePreviousStageSuccess,
                RequireCleanWorkingTree = s.RequireCleanWorkingTree,
                RequireTypedConfirmationForProduction = s.RequireTypedConfirmationForProduction,
                RequiredBranch = string.IsNullOrWhiteSpace(s.RequiredBranch) ? null : s.RequiredBranch!.Trim(),
                Approvers = s.Approvers?.ToList() ?? []
            });
        }

        _db.DeploymentPipelines.Add(pipeline);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Saved pipeline '{Name}' with {Count} stage(s) for {Project}.",
            pipeline.Name, pipeline.Stages.Count, project.Name);

        return Map(pipeline, envs);
    }

    public async Task<PipelineDefinition> BuildDefaultAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct)
            ?? throw new InvalidOperationException("Project not found.");

        var orderedEnvs = project.Environments
            .OrderBy(e => e.DisplayOrder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var stages = new List<StageDefinition>();
        for (var i = 0; i < orderedEnvs.Count; i++)
        {
            var e = orderedEnvs[i];
            stages.Add(new StageDefinition(
                e.Id, e.Name, e.IsProduction, i,
                RequireApproval: e.IsProduction,
                RequirePreviousStageSuccess: i > 0,
                RequireCleanWorkingTree: e.IsProduction,
                RequireTypedConfirmationForProduction: true,
                RequiredBranch: null,
                Approvers: []));
        }

        return new PipelineDefinition(Guid.Empty, projectId,
            $"{project.Name} release pipeline", stages);
    }

    public async Task<bool> DeleteAsync(Guid projectId, CancellationToken ct = default)
    {
        var existing = await _db.DeploymentPipelines
            .Include(p => p.Stages)
            .FirstOrDefaultAsync(p => p.ProjectId == projectId, ct);
        if (existing is null) return false;

        _db.PipelineStages.RemoveRange(existing.Stages);
        _db.DeploymentPipelines.Remove(existing);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private static PipelineDefinition Map(DeploymentPipeline p, IReadOnlyDictionary<Guid, ProjectEnvironment> envs)
    {
        var stages = p.Stages
            .OrderBy(s => s.Order)
            .Select(s =>
            {
                envs.TryGetValue(s.EnvironmentId, out var env);
                return new StageDefinition(
                    s.EnvironmentId,
                    env?.Name ?? "(removed environment)",
                    env?.IsProduction ?? false,
                    s.Order,
                    s.RequireApproval,
                    s.RequirePreviousStageSuccess,
                    s.RequireCleanWorkingTree,
                    s.RequireTypedConfirmationForProduction,
                    s.RequiredBranch,
                    s.Approvers.ToList());
            })
            .ToList();
        return new PipelineDefinition(p.Id, p.ProjectId, p.Name, stages);
    }
}
