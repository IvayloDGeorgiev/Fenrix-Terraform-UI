using Fenrix.IaCStudio.Contracts.Deployments;

namespace Fenrix.IaCStudio.Application.Abstractions.Deployments;

/// <summary>
/// Manages a project's optional pipeline definition — the ordered environment stages plus per-stage gates
/// (approval, required branch, clean tree, production typed-confirm, promote-in-order). A project without a
/// pipeline still deploys per-environment; the pipeline only adds gates to the governed deploy flow.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public interface IPipelineService
{
    /// <summary>The project's pipeline definition, or null when none is configured.</summary>
    Task<PipelineDefinition?> GetAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Creates or replaces the project's pipeline definition with the given ordered stages.</summary>
    Task<PipelineDefinition> SaveAsync(SavePipelineRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns a sensible default pipeline for the project (one stage per environment in display order, with
    /// production gates on production stages) without persisting it — for seeding the editor.
    /// </summary>
    Task<PipelineDefinition> BuildDefaultAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Deletes the project's pipeline definition (deployments continue to work without one).</summary>
    Task<bool> DeleteAsync(Guid projectId, CancellationToken ct = default);
}
