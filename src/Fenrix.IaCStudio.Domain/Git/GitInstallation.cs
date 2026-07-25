namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// A resolved Git binary: its path and, when it could be probed, its reported version string
/// (e.g. "2.43.0"). See docs/08-git-engine.md.
/// </summary>
public sealed record GitInstallation(string ExecutablePath, string? Version)
{
    /// <summary>True when a version was read from <c>git --version</c>.</summary>
    public bool IsUsable => !string.IsNullOrWhiteSpace(ExecutablePath);
}
