using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Per-environment operation lock backed by an on-disk lock file kept inside the project
/// (<c>.fenrix/locks/&lt;env&gt;.lock</c>). Exclusive creation of the file is the lock; the file records the
/// owning process so a crash-orphaned lock (dead PID) is detected as stale and reclaimed. An in-process
/// guard makes same-process double-acquire cheap. See docs/05-terraform-engine.md, docs/06-plan-apply-safety.md.
/// </summary>
public sealed class FileEnvironmentLockService(ILogger<FileEnvironmentLockService> logger) : IEnvironmentLockService
{
    private const string LockExtension = ".lock";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ILogger<FileEnvironmentLockService> _logger = logger;
    private readonly ConcurrentDictionary<Guid, byte> _held = new();

    public async Task<IEnvironmentLock?> TryAcquireAsync(EnvironmentLockRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.LocksDirectory))
            throw new ArgumentException("A locks directory is required.", nameof(request));

        // Cheap same-process guard: if we already hold this environment, deny immediately.
        if (!_held.TryAdd(request.EnvironmentId, 0))
            return null;

        try
        {
            Directory.CreateDirectory(request.LocksDirectory);
            var path = LockPath(request.LocksDirectory, request.EnvironmentId);
            var record = new LockRecord(request.EnvironmentId, request.Operation, Environment.ProcessId, DateTimeOffset.UtcNow);

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    await JsonSerializer.SerializeAsync(stream, record, JsonOptions, ct);
                    _logger.LogInformation("Acquired {Operation} lock for environment {Env}", request.Operation, request.EnvironmentId);
                    return new FileLock(this, path, request.EnvironmentId, request.Operation, record.AcquiredAt);
                }
                catch (IOException) when (File.Exists(path))
                {
                    var existing = ReadInfo(path, request.EnvironmentId);
                    if (existing is { IsStale: false })
                        return Release(request.EnvironmentId, held: true, result: (IEnvironmentLock?)null);

                    _logger.LogWarning("Reclaiming stale lock for environment {Env} (pid {Pid})",
                        request.EnvironmentId, existing?.ProcessId);
                    TryDelete(path); // stale — remove and retry once
                }
            }

            // Could not reclaim within the retry budget; treat as locked.
            return Release(request.EnvironmentId, held: true, result: (IEnvironmentLock?)null);
        }
        catch
        {
            _held.TryRemove(request.EnvironmentId, out _);
            throw;
        }
    }

    public EnvironmentLockInfo? GetActive(Guid environmentId, string locksDirectory)
    {
        if (string.IsNullOrWhiteSpace(locksDirectory))
            return null;
        var path = LockPath(locksDirectory, environmentId);
        return File.Exists(path) ? ReadInfo(path, environmentId) : null;
    }

    public Task<bool> ForceReleaseAsync(Guid environmentId, string locksDirectory, CancellationToken ct = default)
    {
        var path = LockPath(locksDirectory, environmentId);
        var removed = TryDelete(path);
        _held.TryRemove(environmentId, out _);
        if (removed)
            _logger.LogWarning("Force-released lock for environment {Env}", environmentId);
        return Task.FromResult(removed);
    }

    private void ReleaseHeld(Guid environmentId, string path)
    {
        TryDelete(path);
        _held.TryRemove(environmentId, out _);
        _logger.LogInformation("Released lock for environment {Env}", environmentId);
    }

    private T Release<T>(Guid environmentId, bool held, T result)
    {
        if (held)
            _held.TryRemove(environmentId, out _);
        return result;
    }

    private static string LockPath(string locksDirectory, Guid environmentId) =>
        Path.Combine(locksDirectory, $"{environmentId:N}{LockExtension}");

    private EnvironmentLockInfo? ReadInfo(string path, Guid environmentId)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var record = JsonSerializer.Deserialize<LockRecord>(stream, JsonOptions);
            if (record is null)
                return new EnvironmentLockInfo(environmentId, "unknown", 0, DateTimeOffset.MinValue, IsStale: true);
            return new EnvironmentLockInfo(environmentId, record.Operation, record.ProcessId, record.AcquiredAt, IsStale: !IsProcessAlive(record.ProcessId));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable/corrupt lock file → treat as stale so it can be reclaimed.
            return new EnvironmentLockInfo(environmentId, "unknown", 0, DateTimeOffset.MinValue, IsStale: true);
        }
    }

    private static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            using var _ = Process.GetProcessById(pid);
            return true;
        }
        catch (ArgumentException)
        {
            return false; // no such process
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete lock file {Path}", path);
        }
        return false;
    }

    private sealed record LockRecord(Guid EnvironmentId, string Operation, int ProcessId, DateTimeOffset AcquiredAt);

    private sealed class FileLock(FileEnvironmentLockService owner, string path, Guid environmentId, string operation, DateTimeOffset acquiredAt)
        : IEnvironmentLock
    {
        private int _released;

        public Guid EnvironmentId => environmentId;
        public string Operation => operation;
        public DateTimeOffset AcquiredAt => acquiredAt;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                owner.ReleaseHeld(environmentId, path);
            return ValueTask.CompletedTask;
        }
    }
}
