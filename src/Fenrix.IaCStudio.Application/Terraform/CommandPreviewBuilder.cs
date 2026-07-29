using System.Text;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Turns a run spec into the exact <see cref="TerraformCommandRequest"/> the runner executes, and turns
/// that request into a redacted <see cref="CommandPreview"/> for the UI. Because both the request's
/// argument list and the preview come from <see cref="TerraformCommandCatalog"/>, the command shown is
/// exactly the command that runs. See docs/23-command-transparency.md.
/// </summary>
public static class CommandPreviewBuilder
{
    private static readonly IReadOnlyDictionary<string, string> NoEnvironment =
        new Dictionary<string, string>(0);

    /// <summary>Builds the runnable request for a spec against a resolved binary and working directory.</summary>
    public static TerraformCommandRequest BuildRequest(
        TerraformRunSpec spec,
        string executablePath,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        var def = TerraformCommandCatalog.Build(spec);
        return new TerraformCommandRequest(
            spec.ProjectId,
            spec.EnvironmentId,
            spec.Kind,
            executablePath,
            workingDirectory,
            def.Command,
            def.Arguments,
            environmentVariables ?? NoEnvironment,
            def.Risk,
            // fmt - pipes the editor buffer through stdin; it never enters the argument list or the preview.
            StandardInput: spec.StandardInput);
    }

    /// <summary>Builds a redacted, copyable preview of a request, with optional extra context chips.</summary>
    public static CommandPreview BuildPreview(
        TerraformCommandRequest request,
        IEnumerable<CommandContextChip>? extraChips = null)
    {
        var redactedArgs = ArgumentRedactor.RedactArguments(request.Arguments);
        var exeName = Path.GetFileName(request.ExecutablePath);
        if (string.IsNullOrEmpty(exeName))
            exeName = request.ExecutablePath;

        var display = BuildDisplayCommand(exeName, redactedArgs);

        var chips = new List<CommandContextChip> { new("Working dir", request.WorkingDirectory) };
        if (extraChips is not null)
            chips.AddRange(extraChips);

        foreach (var kv in request.EnvironmentVariables)
        {
            var value = ArgumentRedactor.IsSensitiveEnvironmentVariable(kv.Key)
                ? ArgumentRedactor.Placeholder
                : kv.Value;
            chips.Add(new CommandContextChip(kv.Key, value));
        }

        return new CommandPreview(request.ExecutablePath, exeName, redactedArgs, request.WorkingDirectory, chips, display);
    }

    /// <summary>Renders "exe arg1 arg2 …", quoting any token containing whitespace.</summary>
    public static string BuildDisplayCommand(string executableName, IReadOnlyList<string> redactedArguments)
    {
        var sb = new StringBuilder(Quote(executableName));
        foreach (var arg in redactedArguments)
        {
            sb.Append(' ');
            sb.Append(Quote(arg));
        }
        return sb.ToString();
    }

    private static string Quote(string token) =>
        token.Length > 0 && !token.Any(char.IsWhiteSpace) ? token : $"\"{token}\"";
}
