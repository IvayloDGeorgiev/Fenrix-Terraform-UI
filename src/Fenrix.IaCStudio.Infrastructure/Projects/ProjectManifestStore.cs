using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Contracts.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// Reads/writes <c>.fenrix/project-manifest.json</c> using System.Text.Json. Writes are atomic
/// (temp file + replace). Never stores secrets. See docs/03-domain-model.md.
/// </summary>
public sealed class ProjectManifestStore(ILogger<ProjectManifestStore> logger) : IProjectManifestStore
{
    private const string FenrixDir = ".fenrix";
    private const string ManifestFile = "project-manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ILogger<ProjectManifestStore> _logger = logger;

    private static string ManifestPath(string projectRoot) => Path.Combine(projectRoot, FenrixDir, ManifestFile);

    public bool Exists(string projectRoot) => File.Exists(ManifestPath(projectRoot));

    public async Task<ProjectManifest?> ReadAsync(string projectRoot, CancellationToken ct = default)
    {
        var path = ManifestPath(projectRoot);
        if (!File.Exists(path))
            return null;

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ProjectManifest>(stream, JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogWarning(ex, "Could not read project manifest at {Path}", path);
            return null;
        }
    }

    public async Task WriteAsync(string projectRoot, ProjectManifest manifest, CancellationToken ct = default)
    {
        var dir = Path.Combine(projectRoot, FenrixDir);
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, ManifestFile);
        var temp = path + ".tmp";

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), ct);
        File.Move(temp, path, overwrite: true);

        _logger.LogInformation("Wrote project manifest for {Project} to {Path}", manifest.Name, path);
    }
}
