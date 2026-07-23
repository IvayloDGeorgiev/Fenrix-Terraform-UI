namespace Fenrix.IaCStudio.Application.Abstractions;

/// <summary>
/// Resolves the Fenrix data-root layout on disk, creating it (with fallback) if needed.
/// See docs/03-domain-model.md (Windows directory layout).
/// </summary>
public interface IWorkspacePaths
{
    /// <summary>The active data root (configured, default, or fallback).</summary>
    string DataRoot { get; }

    string DataDirectory { get; }
    string LogsDirectory { get; }
    string ProjectsDirectory { get; }
    string CacheDirectory { get; }
    string TempDirectory { get; }
    string ToolsDirectory { get; }
    string BackupsDirectory { get; }

    /// <summary>Full path to the SQLite database file.</summary>
    string DatabaseFilePath { get; }

    /// <summary>True if the primary root could not be used and the LOCALAPPDATA fallback is active.</summary>
    bool UsingFallback { get; }

    /// <summary>Ensures the full directory tree exists. Returns the resolved data root.</summary>
    string EnsureCreated();
}
