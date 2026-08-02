using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Abstractions.Checks;

/// <summary>
/// Estimates cloud cost for an environment with Infracost. Runs <c>infracost breakdown</c> for the projected
/// monthly cost + per-resource breakdown, and <c>infracost diff</c> against a saved baseline for the plan
/// delta. The free Infracost API key is stored in the secret store (never plaintext on disk) and injected as
/// the <c>INFRACOST_API_KEY</c> environment variable at run time — never in args, history, or logs.
/// See docs/34-checks.md.
/// </summary>
public interface ICostEstimationService
{
    /// <summary>Runs <c>infracost breakdown</c> over the environment's working directory (projected monthly cost).</summary>
    Task<CostEstimate> EstimateAsync(
        Guid projectId, Guid environmentId,
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Runs <c>infracost diff</c> against the environment's saved baseline (the delta a change would introduce).
    /// Returns a breakdown result when no baseline exists yet, with a hint to save one.
    /// </summary>
    Task<CostEstimate> DiffAsync(
        Guid projectId, Guid environmentId,
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Saves the current breakdown as the environment's cost baseline (for later diffs).</summary>
    Task<CostEstimate> SaveBaselineAsync(
        Guid projectId, Guid environmentId,
        IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>True when an Infracost API key is present in the secret store.</summary>
    Task<bool> HasApiKeyAsync(CancellationToken ct = default);

    /// <summary>Stores (or overwrites) the Infracost API key in the secret store. Never persisted in plaintext.</summary>
    Task SetApiKeyAsync(string apiKey, CancellationToken ct = default);

    /// <summary>Removes the stored Infracost API key.</summary>
    Task ClearApiKeyAsync(CancellationToken ct = default);
}
