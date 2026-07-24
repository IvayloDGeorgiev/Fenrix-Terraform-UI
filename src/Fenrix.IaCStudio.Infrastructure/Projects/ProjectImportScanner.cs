using System.Text.RegularExpressions;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Contracts.Projects;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Projects;

/// <summary>
/// Read-only scan of an existing folder for the import wizard. Detects Terraform files, a Git repo,
/// likely environment directories, providers, backend config, and a required Terraform version.
/// Never modifies the folder. See docs/03-domain-model.md.
/// </summary>
public sealed partial class ProjectImportScanner(ILogger<ProjectImportScanner> logger) : IProjectImportScanner
{
    private readonly ILogger<ProjectImportScanner> _logger = logger;

    // Common environment directory names, and hints that a folder is an environment root.
    private static readonly string[] EnvHints =
        ["dev", "development", "uat", "test", "staging", "stage", "qa", "live", "prod", "production"];

    private static readonly string[] ProdHints = ["live", "prod", "production"];

    public async Task<ImportScanResult> ScanAsync(string folderPath, CancellationToken ct = default)
    {
        var result = new ImportScanResult { RootPath = folderPath };

        if (!Directory.Exists(folderPath))
            return result;

        result.IsGitRepository = TryFindGitRoot(folderPath, out var gitRoot);
        result.RepositoryRootPath = gitRoot;

        var tfFiles = EnumerateTracked(folderPath, "*.tf").ToList();
        var allConfig = EnumerateTracked(folderPath, "*.tf")
            .Concat(EnumerateTracked(folderPath, "*.tfvars"))
            .Concat(EnumerateTracked(folderPath, "*.hcl"))
            .ToList();

        result.TerraformFileCount = allConfig.Count;
        result.HasBackendConfiguration = await DetectBackendAsync(tfFiles, folderPath, ct);
        result.DetectedProviders = await DetectProvidersAsync(tfFiles, ct);
        result.DetectedTerraformVersion = await DetectRequiredVersionAsync(tfFiles, ct);
        result.SuggestedEnvironments = SuggestEnvironments(folderPath);

        _logger.LogInformation(
            "Scanned {Folder}: {Count} config files, git={Git}, {Envs} suggested environments",
            folderPath, result.TerraformFileCount, result.IsGitRepository, result.SuggestedEnvironments.Count);

        return result;
    }

    private static readonly EnumerationOptions RecursiveOptions = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.System
    };

    private static IEnumerable<string> EnumerateTracked(string root, string pattern)
    {
        foreach (var file in Directory.EnumerateFiles(root, pattern, RecursiveOptions))
        {
            var rel = FileTrackingPolicy.ToRelative(root, file);
            if (!FileTrackingPolicy.IsUnderIgnoredDirectory(rel))
                yield return file;
        }
    }

    private static bool TryFindGitRoot(string start, out string? gitRoot)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                gitRoot = dir.FullName;
                return true;
            }
            dir = dir.Parent;
        }
        gitRoot = null;
        return false;
    }

    private List<EnvironmentMapping> SuggestEnvironments(string root)
    {
        var mappings = new List<EnvironmentMapping>();

        // 1) A conventional environments/ folder.
        var envParent = Path.Combine(root, "environments");
        var candidateDirs = Directory.Exists(envParent)
            ? Directory.EnumerateDirectories(envParent)
            : Directory.EnumerateDirectories(root).Where(d => LooksLikeEnv(Path.GetFileName(d)));

        foreach (var dir in candidateDirs.OrderBy(d => d))
        {
            var name = Path.GetFileName(dir);
            if (FileTrackingPolicy.IsIgnoredDirectory(name))
                continue;

            // Only treat as an environment if it contains Terraform files.
            var hasTf = Directory.EnumerateFiles(dir, "*.tf", SearchOption.TopDirectoryOnly).Any();
            if (!hasTf && !Directory.Exists(envParent))
                continue;

            var tfvars = Directory.EnumerateFiles(dir, "*.tfvars", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).FirstOrDefault();
            var backend = Directory.EnumerateFiles(dir, "backend.hcl", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).FirstOrDefault()
                ?? Directory.EnumerateFiles(dir, "*.hcl", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFileName).FirstOrDefault();

            mappings.Add(new EnvironmentMapping
            {
                Name = TitleCase(name),
                RelativePath = FileTrackingPolicy.ToRelative(root, dir),
                VariablesFile = tfvars,
                BackendConfigFile = backend,
                IsProduction = ProdHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)),
                Include = true
            });
        }

        // 2) Fallback: a single-rooted project with .tf files at the top maps to one "Default" env.
        if (mappings.Count == 0 &&
            Directory.EnumerateFiles(root, "*.tf", SearchOption.TopDirectoryOnly).Any())
        {
            mappings.Add(new EnvironmentMapping
            {
                Name = "Default",
                RelativePath = ".",
                Include = true
            });
        }

        return mappings;
    }

    private static bool LooksLikeEnv(string name) =>
        EnvHints.Any(h => string.Equals(name, h, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(h + "-", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("-" + h, StringComparison.OrdinalIgnoreCase));

    private static async Task<bool> DetectBackendAsync(List<string> tfFiles, string root, CancellationToken ct)
    {
        if (Directory.EnumerateFiles(root, "backend.hcl", RecursiveOptions).Any())
            return true;

        foreach (var file in tfFiles)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            if (BackendBlockRegex().IsMatch(text))
                return true;
        }
        return false;
    }

    private static async Task<List<string>> DetectProvidersAsync(List<string> tfFiles, CancellationToken ct)
    {
        var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in tfFiles)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            foreach (Match m in ProviderBlockRegex().Matches(text))
                providers.Add(m.Groups["name"].Value);
            foreach (Match m in RequiredProviderSourceRegex().Matches(text))
                providers.Add(m.Groups["source"].Value);
        }
        return providers.OrderBy(p => p).ToList();
    }

    private static async Task<string?> DetectRequiredVersionAsync(List<string> tfFiles, CancellationToken ct)
    {
        foreach (var file in tfFiles)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            var m = RequiredVersionRegex().Match(text);
            if (m.Success)
                return m.Groups["ver"].Value.Trim();
        }
        return null;
    }

    private static string TitleCase(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"backend\s+""[^""]+""\s*\{", RegexOptions.IgnoreCase)]
    private static partial Regex BackendBlockRegex();

    [GeneratedRegex(@"provider\s+""(?<name>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderBlockRegex();

    [GeneratedRegex(@"source\s*=\s*""(?<source>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredProviderSourceRegex();

    [GeneratedRegex(@"required_version\s*=\s*""(?<ver>[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex RequiredVersionRegex();
}
