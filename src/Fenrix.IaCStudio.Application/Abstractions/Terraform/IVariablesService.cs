using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// The per-environment variables manager (Phase 12). Reads variable declarations from an environment's
/// configuration and merges them with the environment's tfvars values into a typed, editable view, then writes
/// values back to that environment's tfvars file through the atomic-write + file-history path. See
/// docs/33-variables.md.
/// </summary>
public interface IVariablesService
{
    /// <summary>Loads the merged declaration + value view for an environment.</summary>
    Task<EnvironmentVariables> LoadAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Rewrites the environment's tfvars file from the given values (each <see cref="VariableValueEdit.Raw"/> is
    /// verbatim HCL; null/blank removes the assignment). Uses the atomic-write + file-history path.
    /// </summary>
    Task SaveAsync(Guid projectId, Guid environmentId, IReadOnlyList<VariableValueEdit> edits, CancellationToken ct = default);
}
