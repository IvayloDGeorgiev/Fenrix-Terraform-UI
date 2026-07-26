using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Abstractions.Cloud;

/// <summary>
/// A thin, provider-specific adapter over a cloud account's official CLI (Azure <c>az</c>, AWS <c>aws</c>,
/// Google <c>gcloud</c>). Fenrix does <em>not</em> create a competing credential system: it selects the
/// account and composes the correct process-scoped environment at execution time, resolving any secret
/// just-in-time and discarding it afterwards. Every method takes the transient
/// <see cref="CloudConnectionContext"/> and returns a typed <see cref="ProviderResult{T}"/> rather than
/// throwing, so the UI can surface precise guidance. Mirrors <c>IRepositoryProvider</c>. See
/// docs/10-cloud-integrations.md, docs/11-secrets.md.
/// </summary>
public interface ICloudConnectionProvider
{
    /// <summary>The cloud platform this adapter serves.</summary>
    CloudProviderType ProviderType { get; }

    /// <summary>Confirms the connection can authenticate and returns the identity behind it (the "Test connection" call).</summary>
    Task<ProviderResult<CloudIdentity>> TestAsync(
        CloudConnectionContext context, CancellationToken ct = default);

    /// <summary>
    /// Lists the scopes selectable for this connection (Azure subscriptions, AWS profiles, Google projects),
    /// so the connection dialog can offer them rather than requiring the user to type an id.
    /// </summary>
    Task<ProviderResult<IReadOnlyList<CloudScope>>> GetAvailableScopesAsync(
        CloudConnectionContext context, CancellationToken ct = default);

    /// <summary>
    /// Composes the process-scoped environment variables Terraform needs to authenticate to this account
    /// (e.g. <c>ARM_*</c>, <c>AWS_PROFILE</c>/<c>AWS_REGION</c>, <c>GOOGLE_*</c>). Any secret is resolved
    /// just-in-time from <see cref="CloudConnectionContext.Secret"/> and never persisted. The returned map
    /// is applied to the child process only, then discarded.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> BuildEnvironmentAsync(
        CloudConnectionContext context, CancellationToken ct = default);
}
