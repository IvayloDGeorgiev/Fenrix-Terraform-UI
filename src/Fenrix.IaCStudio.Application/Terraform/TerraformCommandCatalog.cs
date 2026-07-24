using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Builds the exact, ordered argument list for each typed command. This is the <em>single</em> source
/// of truth for what Terraform is invoked with: both the live preview and the executed process are
/// generated from the list returned here, so they can never diverge. The first element is always the
/// Terraform subcommand. See docs/05-terraform-engine.md and docs/23-command-transparency.md.
/// </summary>
public static class TerraformCommandCatalog
{
    /// <summary>The resolved subcommand, its full argument list (subcommand first), and its risk level.</summary>
    public readonly record struct CommandDefinition(
        string Command,
        IReadOnlyList<string> Arguments,
        TerraformRiskLevel Risk);

    public static CommandDefinition Build(TerraformRunSpec spec) => spec.Kind switch
    {
        TerraformCommandKind.Version => BuildVersion(),
        TerraformCommandKind.Init => BuildInit(spec.Init),
        TerraformCommandKind.Format => BuildFormat(spec.Format),
        TerraformCommandKind.Validate => BuildValidate(spec.Validate),
        _ => throw new ArgumentOutOfRangeException(nameof(spec), spec.Kind, "Unsupported command kind.")
    };

    private static CommandDefinition BuildVersion() =>
        new("version", ["version", "-json"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildInit(InitOptions o)
    {
        var args = new List<string> { "init", "-input=false" };
        if (o.Upgrade) args.Add("-upgrade");
        if (o.Reconfigure) args.Add("-reconfigure");
        if (o.DisableBackend) args.Add("-backend=false");
        if (!string.IsNullOrWhiteSpace(o.BackendConfigFile))
            args.Add($"-backend-config={o.BackendConfigFile}");
        // Init downloads providers/modules and touches the backend, but changes no real infrastructure.
        return new CommandDefinition("init", args, TerraformRiskLevel.Safe);
    }

    private static CommandDefinition BuildFormat(FormatOptions o)
    {
        var args = new List<string> { "fmt" };
        if (o.CheckOnly) args.Add("-check");
        if (o.ShowDiff) args.Add("-diff");
        if (o.Recursive) args.Add("-recursive");
        // Check mode reads only; write mode rewrites files on disk (never real infrastructure).
        var risk = o.CheckOnly ? TerraformRiskLevel.ReadOnly : TerraformRiskLevel.Safe;
        return new CommandDefinition("fmt", args, risk);
    }

    private static CommandDefinition BuildValidate(ValidateOptions o)
    {
        var args = new List<string> { "validate" };
        if (o.Json) args.Add("-json");
        return new CommandDefinition("validate", args, TerraformRiskLevel.ReadOnly);
    }
}
