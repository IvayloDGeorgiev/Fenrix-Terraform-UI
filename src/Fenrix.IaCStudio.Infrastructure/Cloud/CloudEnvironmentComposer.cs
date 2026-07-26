using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Contracts.Cloud;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Cloud;

/// <summary>
/// Composes the credential environment for an environment's bound cloud connection at execution time and a
/// non-secret identity label for the command-preview chip / history. Resolves the connection + secret
/// just-in-time via the factory and hands the process-scoped variables to the caller (plan/apply). Returns
/// <see cref="CloudEnvironmentResult.None"/> when nothing is bound so callers can block state-changing ops.
/// See docs/25-execution-lifecycle.md, docs/26-connections.md.
/// </summary>
public sealed class CloudEnvironmentComposer(
    ICloudConnectionProviderFactory factory,
    ILogger<CloudEnvironmentComposer> logger) : ICloudEnvironmentComposer
{
    private readonly ICloudConnectionProviderFactory _factory = factory;
    private readonly ILogger<CloudEnvironmentComposer> _logger = logger;

    public async Task<CloudEnvironmentResult> ComposeAsync(Guid? cloudConnectionId, CancellationToken ct = default)
    {
        if (cloudConnectionId is not { } id)
            return CloudEnvironmentResult.None;

        var resolved = await _factory.ResolveAsync(id, ct);
        if (resolved is null)
        {
            _logger.LogWarning("Cloud connection {Id} could not be resolved (missing or no adapter).", id);
            return CloudEnvironmentResult.None;
        }

        var (provider, context) = resolved.Value;
        IReadOnlyDictionary<string, string> env;
        try
        {
            env = await provider.BuildEnvironmentAsync(context, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compose cloud environment for connection {Id}.", id);
            env = new Dictionary<string, string>(0);
        }

        return new CloudEnvironmentResult(true, id, context.DisplayName, BuildIdentityChip(context), env);
    }

    /// <summary>A short, non-secret account label for the preview chip, e.g. <c>azure:sub-123</c>, <c>aws:prod/eu-west-1</c>, <c>gcp:my-project</c>.</summary>
    internal static string BuildIdentityChip(CloudConnectionContext c) => c.ProviderType switch
    {
        CloudProviderType.Azure =>
            "azure:" + (First(c.SubscriptionOrProjectId, c.TenantOrAccountId, c.DisplayName)),
        CloudProviderType.Aws =>
            "aws:" + First(c.ProfileName, c.TenantOrAccountId, c.DisplayName)
                   + (string.IsNullOrWhiteSpace(c.Region) ? "" : "/" + c.Region!.Trim()),
        CloudProviderType.GoogleCloud =>
            "gcp:" + First(c.SubscriptionOrProjectId, c.DisplayName),
        _ => c.DisplayName
    };

    private static string First(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v))
                return v!.Trim();
        return "unknown";
    }
}
