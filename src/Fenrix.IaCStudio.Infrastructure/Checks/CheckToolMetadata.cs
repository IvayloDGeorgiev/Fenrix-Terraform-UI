using Fenrix.IaCStudio.Application.Settings;
using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Static per-tool facts shared by discovery and the installer: the settings key that overrides the binary
/// path, the candidate executable names, the version-probe arguments, and the GitHub repository the installer
/// downloads releases from. Keeps the tool table in one place. See docs/34-checks.md.
/// </summary>
internal static class CheckToolMetadata
{
    internal sealed record ToolInfo(
        CheckTool Tool,
        string SettingKey,
        string DisplayName,
        string BaseExecutableName,   // without extension
        string GitHubOwner,
        string GitHubRepo,
        string AssetOs,              // token used in the release asset name
        string ArchiveKind);         // "zip" or "targz"

    private static readonly IReadOnlyDictionary<CheckTool, ToolInfo> Map = new Dictionary<CheckTool, ToolInfo>
    {
        [CheckTool.TfLint] = new(CheckTool.TfLint, FenrixSettingKeys.TfLintExecutable, "TFLint",
            "tflint", "terraform-linters", "tflint", "windows", "zip"),
        [CheckTool.Tfsec] = new(CheckTool.Tfsec, FenrixSettingKeys.TfsecExecutable, "tfsec",
            "tfsec", "aquasecurity", "tfsec", "windows", "exe"),
        [CheckTool.Trivy] = new(CheckTool.Trivy, FenrixSettingKeys.TrivyExecutable, "Trivy",
            "trivy", "aquasecurity", "trivy", "windows", "zip"),
        [CheckTool.Infracost] = new(CheckTool.Infracost, FenrixSettingKeys.InfracostExecutable, "Infracost",
            "infracost", "infracost", "infracost", "windows", "exe"),
    };

    internal static ToolInfo For(CheckTool tool) => Map[tool];

    /// <summary>Candidate on-disk executable names, most specific first (Windows adds the .exe variant).</summary>
    internal static IReadOnlyList<string> ExecutableNames(CheckTool tool)
    {
        var baseName = Map[tool].BaseExecutableName;
        return OperatingSystem.IsWindows() ? [baseName + ".exe", baseName] : [baseName];
    }

    internal static string SettingKey(CheckTool tool) => Map[tool].SettingKey;

    internal static string DisplayName(CheckTool tool) => Map[tool].DisplayName;

    /// <summary>Every tool exposes <c>--version</c>.</summary>
    internal static IReadOnlyList<string> VersionArguments(CheckTool _) => ["--version"];
}
