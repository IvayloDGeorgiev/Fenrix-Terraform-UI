using Fenrix.IaCStudio.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Workspace;

/// <summary>
/// Resolves and creates the Fenrix data-root tree. Prefers the configured/primary root
/// (C:\FenrixSource\FenrixIaCStudio); if that cannot be written, falls back to
/// %LOCALAPPDATA%\FenrixSource\FenrixIaCStudio. See docs/03-domain-model.md.
/// </summary>
public sealed class WorkspacePaths : IWorkspacePaths
{
    private const string PrimaryRoot = @"C:\FenrixSource\FenrixIaCStudio";
    private const string VendorFolder = "FenrixSource";
    private const string AppFolder = "FenrixIaCStudio";

    private readonly ILogger<WorkspacePaths> _logger;
    private readonly string? _overrideRoot;
    private string _dataRoot;

    public WorkspacePaths(ILogger<WorkspacePaths> logger, string? overrideRoot = null)
    {
        _logger = logger;
        _overrideRoot = string.IsNullOrWhiteSpace(overrideRoot) ? null : overrideRoot;
        _dataRoot = _overrideRoot ?? PrimaryRoot;
    }

    public string DataRoot => _dataRoot;
    public bool UsingFallback { get; private set; }

    public string DataDirectory => Path.Combine(_dataRoot, "Data");
    public string LogsDirectory => Path.Combine(_dataRoot, "Logs");
    public string ProjectsDirectory => Path.Combine(_dataRoot, "Projects");
    public string CacheDirectory => Path.Combine(_dataRoot, "Cache");
    public string TempDirectory => Path.Combine(_dataRoot, "Temp");
    public string ToolsDirectory => Path.Combine(_dataRoot, "Tools");
    public string BackupsDirectory => Path.Combine(_dataRoot, "Backups");
    public string DatabaseFilePath => Path.Combine(DataDirectory, "fenrix.db");

    public string EnsureCreated()
    {
        if (TryCreateTree(_dataRoot))
            return _dataRoot;

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            VendorFolder, AppFolder);

        _logger.LogWarning(
            "Could not use data root {Primary}; falling back to {Fallback}", _dataRoot, fallback);

        if (!TryCreateTree(fallback))
            throw new IOException($"Unable to create the Fenrix data root at '{_dataRoot}' or '{fallback}'.");

        _dataRoot = fallback;
        UsingFallback = _overrideRoot is null;
        return _dataRoot;
    }

    private bool TryCreateTree(string root)
    {
        try
        {
            foreach (var dir in new[]
            {
                root, Path.Combine(root, "Data"), Path.Combine(root, "Data", "migrations"),
                Path.Combine(root, "Logs"), Path.Combine(root, "Logs", "application"),
                Path.Combine(root, "Logs", "terraform"), Path.Combine(root, "Logs", "git"),
                Path.Combine(root, "Logs", "diagnostics"),
                Path.Combine(root, "Projects"),
                Path.Combine(root, "Cache"), Path.Combine(root, "Temp"),
                Path.Combine(root, "Tools"), Path.Combine(root, "Backups")
            })
            {
                Directory.CreateDirectory(dir);
            }

            // verify writability
            var probe = Path.Combine(root, ".write-probe");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or NotSupportedException)
        {
            return false;
        }
    }
}
