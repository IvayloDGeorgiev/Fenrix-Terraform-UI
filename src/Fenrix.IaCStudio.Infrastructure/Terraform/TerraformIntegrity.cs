using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Projects;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Shared, project-local path resolution and integrity hashing for the plan/apply services. Plans and
/// locks live inside the project folder (<c>plans/&lt;env&gt;/</c>, <c>.fenrix/locks/</c>) so everything for a
/// project stays in one place. See docs/06-plan-apply-safety.md.
/// </summary>
internal static class TerraformIntegrity
{
    /// <summary>The environment's Terraform working directory (absolute), falling back to the project root.</summary>
    public static string ResolveWorkingDirectory(InfrastructureProject project, ProjectEnvironment? environment)
    {
        var wd = environment?.WorkingDirectory;
        if (string.IsNullOrWhiteSpace(wd))
            return project.RootPath;
        return Path.IsPathRooted(wd) ? wd : Path.Combine(project.RootPath, wd);
    }

    /// <summary>The project-local directory holding per-environment operation lock files.</summary>
    public static string LocksDirectory(InfrastructureProject project) =>
        Path.Combine(project.RootPath, ".fenrix", "locks");

    /// <summary>The project-local directory holding saved plan files for one environment.</summary>
    public static string PlansDirectory(InfrastructureProject project, string environmentSlug) =>
        Path.Combine(project.RootPath, "plans", environmentSlug);

    /// <summary>
    /// Combined SHA-256 over the environment's configuration files (the working-directory subtree plus the
    /// project's shared <c>modules/</c>), used to invalidate a plan when configuration changes. Modules
    /// referenced from outside these locations are not covered (documented limitation).
    /// </summary>
    public static async Task<string> ComputeConfigHashAsync(string projectRoot, string workingDirectory, CancellationToken ct)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        await AddDirectoryAsync(projectRoot, workingDirectory, map, ct);

        var modules = Path.Combine(projectRoot, "modules");
        if (Directory.Exists(modules))
            await AddDirectoryAsync(projectRoot, modules, map, ct);

        return PlanIntegrity.CombineConfigHashes(map);
    }

    /// <summary>SHA-256 of the provider lock file in the working directory, or null when absent.</summary>
    public static async Task<string?> ComputeLockHashAsync(string workingDirectory, CancellationToken ct)
    {
        var lockPath = Path.Combine(workingDirectory, PlanIntegrity.LockFileName);
        return File.Exists(lockPath) ? await FileHashing.Sha256HexAsync(lockPath, ct) : null;
    }

    private static async Task AddDirectoryAsync(string root, string dir, Dictionary<string, string> map, CancellationToken ct)
    {
        if (!Directory.Exists(dir))
            return;

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = FileTrackingPolicy.ToRelative(root, file);
            if (FileTrackingPolicy.IsUnderIgnoredDirectory(rel))
                continue;
            if (!PlanIntegrity.IsConfigurationFile(rel))
                continue;
            if (map.ContainsKey(rel))
                continue;
            map[rel] = await FileHashing.Sha256HexAsync(file, ct);
        }
    }
}
