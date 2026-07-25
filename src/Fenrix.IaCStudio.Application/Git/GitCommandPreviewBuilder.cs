using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Turns a <see cref="GitCommandCatalog.GitCommandDefinition"/> into the exact <see cref="GitCommandRequest"/>
/// the runner executes, and into a redacted <see cref="CommandPreview"/> for the UI. Because both come from
/// the same argument list, the command shown is exactly the command that runs. Remote URLs have their
/// credentials redacted. Reuses the shared <see cref="CommandPreview"/> so one preview component serves both
/// Terraform and Git. See docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public static class GitCommandPreviewBuilder
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment = new Dictionary<string, string>(0);

    public static GitCommandRequest BuildRequest(
        GitCommandCatalog.GitCommandDefinition def,
        Guid projectId,
        string executablePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null) =>
        new(projectId, def.Kind, executablePath, workingDirectory, def.Command, def.Arguments,
            environmentVariables ?? NoEnvironment, def.Risk);

    /// <summary>Builds a redacted, copyable preview of a request with optional extra context chips.</summary>
    public static CommandPreview BuildPreview(
        GitCommandRequest request,
        IEnumerable<CommandContextChip>? extraChips = null)
    {
        var redactedArgs = RedactArguments(request.Arguments);
        var exeName = Path.GetFileName(request.ExecutablePath);
        if (string.IsNullOrEmpty(exeName))
            exeName = request.ExecutablePath;

        var display = CommandPreviewBuilder.BuildDisplayCommand(exeName, redactedArgs);

        var chips = new List<CommandContextChip> { new("Working dir", request.WorkingDirectory) };
        if (extraChips is not null)
            chips.AddRange(extraChips);

        return new CommandPreview(request.ExecutablePath, exeName, redactedArgs, request.WorkingDirectory, chips, display);
    }

    /// <summary>Human label for a Git operation risk, matching the Terraform preview vocabulary.</summary>
    public static string RiskLabel(GitOperationRisk risk) => risk switch
    {
        GitOperationRisk.ReadOnly => "read-only",
        GitOperationRisk.Safe => "safe",
        GitOperationRisk.StateChanging => "state-changing",
        GitOperationRisk.Destructive => "destructive",
        _ => risk.ToString()
    };

    /// <summary>Redacts credentials in URL arguments and any key=value secret, preserving order.</summary>
    public static IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments)
    {
        var result = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
        {
            var arg = arguments[i];
            arg = GitUrlRedactor.LooksLikeCredentialedUrl(arg) ? GitUrlRedactor.Redact(arg) : arg;
            result[i] = ArgumentRedactor.RedactArgument(arg);
        }
        return result;
    }
}
