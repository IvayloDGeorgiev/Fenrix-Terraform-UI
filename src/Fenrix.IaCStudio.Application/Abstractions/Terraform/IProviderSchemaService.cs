using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Provides the machine-readable provider/resource/data-source schemas that back the visual builder. Schemas
/// are captured once per environment by running <c>terraform providers schema -json</c> and cached offline
/// under <c>Cache/terraform-schemas</c>, so the builder works without re-running Terraform on every form.
/// The refresh is a read-only command (never takes the environment lock, not gated on a cloud connection).
/// See docs/07-visual-builder.md.
/// </summary>
public interface IProviderSchemaService
{
    /// <summary>Builds the redacted command preview for the schema refresh (<c>providers schema -json</c>).</summary>
    Task<InspectionContext> PreviewAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Runs <c>providers schema -json</c> for the environment, writes the raw JSON to the offline cache, and
    /// returns the parsed set. Streams human-readable progress through <paramref name="output"/>.
    /// </summary>
    Task<SchemaRefreshResult> RefreshAsync(
        Guid projectId, Guid environmentId, IProgress<ProcessOutputEvent>? output = null, CancellationToken ct = default);

    /// <summary>Loads the cached schema set for an environment, or <see cref="ProviderSchemaSet.Empty"/> when none is cached.</summary>
    Task<ProviderSchemaSet> GetCachedAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>Reports the state of the on-disk cache (present, when captured, provider count, lock hash).</summary>
    Task<ProviderSchemaCacheInfo> GetCacheInfoAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);
}
