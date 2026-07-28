namespace Fenrix.IaCStudio.Contracts.Deployments;

/// <summary>One stage of a pipeline definition, projected for the editor/board (environment + its gates).</summary>
public sealed record StageDefinition(
    Guid EnvironmentId,
    string EnvironmentName,
    bool IsProduction,
    int Order,
    bool RequireApproval,
    bool RequirePreviousStageSuccess,
    bool RequireCleanWorkingTree,
    bool RequireTypedConfirmationForProduction,
    string? RequiredBranch,
    IReadOnlyList<string> Approvers);

/// <summary>A per-project ordered release pipeline definition. See docs/20-pipelines-deployments.md.</summary>
public sealed record PipelineDefinition(
    Guid Id,
    Guid ProjectId,
    string Name,
    IReadOnlyList<StageDefinition> Stages);

/// <summary>The editable input for one stage when saving a pipeline.</summary>
public sealed record StageDefinitionInput(
    Guid EnvironmentId,
    bool RequireApproval = false,
    bool RequirePreviousStageSuccess = false,
    bool RequireCleanWorkingTree = false,
    bool RequireTypedConfirmationForProduction = true,
    string? RequiredBranch = null,
    IReadOnlyList<string>? Approvers = null);

/// <summary>Create or replace the project's pipeline definition with the given ordered stages.</summary>
public sealed record SavePipelineRequest(
    Guid ProjectId,
    string Name,
    IReadOnlyList<StageDefinitionInput> Stages);
