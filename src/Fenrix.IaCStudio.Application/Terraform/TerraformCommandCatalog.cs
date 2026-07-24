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
        TerraformCommandKind.Plan => BuildPlan(spec),
        TerraformCommandKind.Apply => BuildApply(spec),
        TerraformCommandKind.Show => BuildShow(spec),
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

    private static CommandDefinition BuildPlan(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.OutPlanFile))
            throw new InvalidOperationException("A plan requires an output path (spec.OutPlanFile).");

        var o = spec.Plan;
        var args = new List<string> { "plan", "-input=false" };
        // -destroy and -refresh-only are mutually exclusive; Destroy wins if both are set.
        if (o.Destroy) args.Add("-destroy");
        else if (o.RefreshOnly) args.Add("-refresh-only");
        if (o.Parallelism is int p and > 0) args.Add($"-parallelism={p}");
        if (!string.IsNullOrWhiteSpace(spec.VarFile)) args.Add($"-var-file={spec.VarFile}");
        args.Add($"-out={spec.OutPlanFile}");

        // Planning changes no real infrastructure — it only writes the plan file (state is refreshed
        // in memory, not persisted). The destructive weight is carried at apply time.
        return new CommandDefinition("plan", args, TerraformRiskLevel.Safe);
    }

    private static CommandDefinition BuildApply(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.PlanFilePath))
            throw new InvalidOperationException("Apply requires a saved plan file (spec.PlanFilePath).");

        // The saved plan already fixes every variable, so no -var-file is passed (Terraform rejects it
        // with a saved plan). -json yields the structured event stream for per-resource progress. The
        // plan file is the final positional argument.
        var args = new List<string> { "apply", "-input=false", "-json" };
        if (spec.Plan.Parallelism is int p and > 0) args.Add($"-parallelism={p}");
        args.Add(spec.PlanFilePath);

        // Applying a saved plan executes real changes; the UI elevates this to "destructive" when the
        // plan contains deletions or replacements.
        return new CommandDefinition("apply", args, TerraformRiskLevel.StateChanging);
    }

    private static CommandDefinition BuildShow(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.PlanFilePath))
            throw new InvalidOperationException("Show requires a plan file (spec.PlanFilePath).");
        // Read-only conversion of a saved plan to JSON for review.
        return new CommandDefinition("show", ["show", "-json", spec.PlanFilePath], TerraformRiskLevel.ReadOnly);
    }
}
