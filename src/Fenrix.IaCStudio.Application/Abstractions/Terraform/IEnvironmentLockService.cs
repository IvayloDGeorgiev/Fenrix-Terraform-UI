namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>A request to acquire the operation lock for one environment.</summary>
/// <param name="EnvironmentId">The environment being locked.</param>
/// <param name="LocksDirectory">The directory the lock file lives in (project-local, e.g. <c>.fenrix/locks</c>).</param>
/// <param name="Operation">A short label for what holds the lock (e.g. "plan", "apply", "destroy").</param>
public readonly record struct EnvironmentLockRequest(Guid EnvironmentId, string LocksDirectory, string Operation);

/// <summary>Details of an active lock, read from its on-disk lock file.</summary>
public sealed record EnvironmentLockInfo(
    Guid EnvironmentId,
    string Operation,
    int ProcessId,
    DateTimeOffset AcquiredAt,
    bool IsStale);

/// <summary>
/// A held environment lock. Disposing it releases the lock (deletes the lock file). See
/// docs/05-terraform-engine.md and docs/06-plan-apply-safety.md.
/// </summary>
public interface IEnvironmentLock : IAsyncDisposable
{
    Guid EnvironmentId { get; }
    string Operation { get; }
    DateTimeOffset AcquiredAt { get; }
}

/// <summary>
/// Enforces "only one state-changing operation per environment at a time" via an on-disk lock file kept
/// inside the project (so it survives an app crash and is visible/force-releasable). Read-only inspection
/// does not take the lock. See docs/06-plan-apply-safety.md and docs/25-execution-lifecycle.md.
/// </summary>
public interface IEnvironmentLockService
{
    /// <summary>
    /// Tries to acquire the environment lock. Returns the held lock on success, or <c>null</c> if the
    /// environment is already locked by a live operation. A stale lock (dead PID) is reclaimed.
    /// </summary>
    Task<IEnvironmentLock?> TryAcquireAsync(EnvironmentLockRequest request, CancellationToken ct = default);

    /// <summary>Reads the current lock for an environment, or <c>null</c> when it is free.</summary>
    EnvironmentLockInfo? GetActive(Guid environmentId, string locksDirectory);

    /// <summary>Force-releases a lock (e.g. after a crash left it behind). Returns true if a lock was removed.</summary>
    Task<bool> ForceReleaseAsync(Guid environmentId, string locksDirectory, CancellationToken ct = default);
}
