using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Shared config-template library (the reusable templates deferred from Phase 10). Managed behind
/// <see cref="Domain.Enterprise.Permission.ManageTemplates"/>. Applying instantiates the template (pure) and
/// writes the HCL to a project's <c>.tf</c> file through the atomic-write + file-history path; templates author
/// config only, never state. Stored in the metadata DB so a team shares one library. See docs/29-enterprise.md.
/// </summary>
public interface ITemplateService
{
    Task<IReadOnlyList<TemplateSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<TemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken = default);
    Task<TemplateDetail> SaveAsync(SaveTemplateRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid templateId, CancellationToken cancellationToken = default);

    /// <summary>Instantiates without writing — for a live preview + missing/unknown-placeholder feedback.</summary>
    Task<ApplyTemplateResult> PreviewAsync(
        Guid templateId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default);

    /// <summary>Instantiates and appends the result to the target file (atomic + versioned); audits the apply.</summary>
    Task<ApplyTemplateResult> ApplyAsync(ApplyTemplateRequest request, CancellationToken cancellationToken = default);
}
