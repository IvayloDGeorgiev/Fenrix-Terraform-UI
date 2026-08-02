namespace Fenrix.IaCStudio.Contracts.Projects;

/// <summary>
/// Outcome of re-scanning the Fenrix projects directory (Phase 12). Reports how many project folders were newly
/// registered and how many stale registrations (whose folder was deleted on disk) were removed. Files are never
/// modified. See docs/03-domain-model.md.
/// </summary>
/// <param name="Added">Project folders found on disk and newly registered.</param>
/// <param name="Removed">Workspace projects whose folder no longer exists, unregistered.</param>
/// <param name="Scanned">Candidate folders inspected under the projects directory.</param>
public sealed record ProjectRescanResult(int Added, int Removed, int Scanned);
