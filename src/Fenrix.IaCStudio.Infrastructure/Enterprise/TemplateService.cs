using Fenrix.IaCStudio.Application.Abstractions.Authoring;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed shared template library. CRUD persists templates + parameters; apply instantiates (pure) and
/// writes through the Phase 10 authoring service, so the write is atomic, journalled, and versioned — the same
/// path the visual builder/editor use (ADR-0002). Applies are audited. See docs/29-enterprise.md.
/// </summary>
public sealed class TemplateService(
    AppDbContext db,
    IConfigAuthoringService authoring,
    IUserContext userContext,
    IAuthorizationService authorization,
    IAuditService audit) : ITemplateService
{
    private readonly AppDbContext _db = db;
    private readonly IConfigAuthoringService _authoring = authoring;
    private readonly IUserContext _userContext = userContext;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly IAuditService _audit = audit;

    private async Task RequireManageTemplatesAsync(string target, CancellationToken ct)
    {
        var result = await _authorization.AuthorizeAsync(Permission.ManageTemplates, target: target, cancellationToken: ct);
        if (!result.Allowed)
            throw new UnauthorizedAccessException(result.Reason ?? "You need the 'ManageTemplates' permission.");
    }

    public async Task<IReadOnlyList<TemplateSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.ConfigTemplates.AsNoTracking()
            .Include(t => t.Parameters)
            .OrderBy(t => t.Category).ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
        return rows.Select(t => new TemplateSummary(
            t.Id, t.Name, t.Description, t.Category, t.Parameters.Count, t.UpdatedAt)).ToList();
    }

    public async Task<TemplateDetail?> GetAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        var t = await _db.ConfigTemplates.AsNoTracking()
            .Include(x => x.Parameters)
            .FirstOrDefaultAsync(x => x.Id == templateId, cancellationToken);
        return t is null ? null : MapDetail(t);
    }

    public async Task<TemplateDetail> SaveAsync(SaveTemplateRequest request, CancellationToken cancellationToken = default)
    {
        await RequireManageTemplatesAsync(request.Name, cancellationToken);
        ConfigTemplate template;
        if (request.Id is { } id)
        {
            template = await _db.ConfigTemplates.Include(t => t.Parameters)
                .FirstOrDefaultAsync(t => t.Id == id, cancellationToken)
                ?? throw new InvalidOperationException("Template not found.");
            _db.TemplateParameters.RemoveRange(template.Parameters);
            template.Parameters.Clear();
        }
        else
        {
            template = new ConfigTemplate { CreatedBy = _userContext.Current.DisplayName };
            _db.ConfigTemplates.Add(template);
        }

        template.Name = request.Name.Trim();
        template.Description = request.Description?.Trim();
        template.Category = request.Category?.Trim();
        template.Body = request.Body;
        template.DefaultTargetFile = request.DefaultTargetFile?.Trim();
        template.UpdatedAt = DateTimeOffset.UtcNow;
        template.Parameters = request.Parameters.Select((p, ix) => new TemplateParameter
        {
            TemplateId = template.Id,
            Name = p.Name.Trim(),
            Description = p.Description?.Trim(),
            Type = p.Type,
            DefaultValue = p.DefaultValue,
            Required = p.Required,
            DisplayOrder = p.DisplayOrder == 0 ? ix : p.DisplayOrder
        }).ToList();

        await _db.SaveChangesAsync(cancellationToken);
        return MapDetail(template);
    }

    public async Task DeleteAsync(Guid templateId, CancellationToken cancellationToken = default)
    {
        await RequireManageTemplatesAsync(templateId.ToString(), cancellationToken);
        var template = await _db.ConfigTemplates.Include(t => t.Parameters)
            .FirstOrDefaultAsync(t => t.Id == templateId, cancellationToken);
        if (template is null) return;
        _db.ConfigTemplates.Remove(template);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApplyTemplateResult> PreviewAsync(
        Guid templateId, IReadOnlyDictionary<string, string?> values, CancellationToken cancellationToken = default)
    {
        var template = await LoadEntityAsync(templateId, cancellationToken);
        if (template is null) return ApplyTemplateResult.Fail("Template not found.");

        var result = TemplateInstantiator.Instantiate(template, values);
        return new ApplyTemplateResult(
            result.Ok, result.Hcl, result.MissingRequired, result.Unknown, template.DefaultTargetFile, null);
    }

    public async Task<ApplyTemplateResult> ApplyAsync(
        ApplyTemplateRequest request, CancellationToken cancellationToken = default)
    {
        var template = await LoadEntityAsync(request.TemplateId, cancellationToken);
        if (template is null) return ApplyTemplateResult.Fail("Template not found.");

        var result = TemplateInstantiator.Instantiate(template, request.Values);
        if (!result.Ok)
            return new ApplyTemplateResult(false, result.Hcl, result.MissingRequired, result.Unknown,
                request.TargetRelativePath, "Fill in all required parameters before applying.");

        var target = string.IsNullOrWhiteSpace(request.TargetRelativePath)
            ? template.DefaultTargetFile ?? "main.tf"
            : request.TargetRelativePath;

        var write = await _authoring.AppendAsync(request.ProjectId, target, result.Hcl, cancellationToken);
        if (!write.Success)
            return new ApplyTemplateResult(false, result.Hcl, result.MissingRequired, result.Unknown, target, write.Error);

        await _audit.WriteAsync(new AuditEntry(
            AuditAction.TemplateApplied, ProjectId: request.ProjectId,
            Target: template.Name, Detail: $"Applied template to {target}."), cancellationToken);

        return new ApplyTemplateResult(true, result.Hcl, [], result.Unknown, target, null);
    }

    private Task<ConfigTemplate?> LoadEntityAsync(Guid id, CancellationToken ct)
        => _db.ConfigTemplates.AsNoTracking().Include(t => t.Parameters).FirstOrDefaultAsync(t => t.Id == id, ct);

    private static TemplateDetail MapDetail(ConfigTemplate t) => new(
        t.Id, t.Name, t.Description, t.Category, t.Body, t.DefaultTargetFile,
        t.Parameters.OrderBy(p => p.DisplayOrder)
            .Select(p => new TemplateParameterModel(p.Name, p.Description, p.Type, p.DefaultValue, p.Required, p.DisplayOrder))
            .ToList(),
        t.UpdatedAt);
}
