using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Application.Abstractions.Git;

/// <summary>
/// Discovers the Git binary and its version. Resolution prefers the configured executable (Settings
/// <c>git.executable</c>, project scope first) and falls back to the system <c>PATH</c>. Mirrors the
/// Terraform discovery pattern. See docs/08-git-engine.md.
/// </summary>
public interface IGitDiscovery
{
    /// <summary>Resolves a usable Git binary, or null when none is configured or on PATH.</summary>
    Task<GitInstallation?> ResolveAsync(Guid? projectId = null, CancellationToken ct = default);
}
