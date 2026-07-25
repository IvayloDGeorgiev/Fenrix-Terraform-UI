namespace Fenrix.IaCStudio.Application.Abstractions.Git;

/// <summary>
/// A narrow capability to initialise a Git repository in a directory, kept separate from
/// <see cref="IGitService"/> so project creation can use it without depending on the full Git façade (which
/// itself depends on the project service — separating this avoids a DI cycle). See docs/08-git-engine.md.
/// </summary>
public interface IGitRepositoryInitializer
{
    /// <summary>Runs <c>git init</c> in <paramref name="directory"/>. Returns true on success.</summary>
    Task<bool> InitializeAsync(string directory, CancellationToken ct = default);
}
