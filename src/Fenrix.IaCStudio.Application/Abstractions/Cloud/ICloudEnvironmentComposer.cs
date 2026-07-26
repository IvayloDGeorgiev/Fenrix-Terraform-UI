using Fenrix.IaCStudio.Contracts.Cloud;

namespace Fenrix.IaCStudio.Application.Abstractions.Cloud;

/// <summary>
/// Bridges a bound cloud connection into Terraform execution: given an environment's
/// <c>CloudConnectionId</c>, it resolves the connection, composes the process-scoped credential environment
/// (secret resolved just-in-time), and produces a non-secret identity label for the command-preview chip and
/// history. Returns <see cref="CloudEnvironmentResult.None"/> when no connection is bound — callers then
/// block state-changing operations (authentication required). See docs/25-execution-lifecycle.md,
/// docs/26-connections.md.
/// </summary>
public interface ICloudEnvironmentComposer
{
    /// <summary>Composes the credential environment for an environment's bound connection (or None when unbound).</summary>
    Task<CloudEnvironmentResult> ComposeAsync(Guid? cloudConnectionId, CancellationToken ct = default);
}
