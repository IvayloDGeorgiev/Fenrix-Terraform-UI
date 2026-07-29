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

        // ---- Phase 9: state & inspection ----
        TerraformCommandKind.StateList => BuildStateList(),
        TerraformCommandKind.StateShow => BuildStateShow(),
        TerraformCommandKind.Output => BuildOutput(spec),
        TerraformCommandKind.Graph => BuildGraph(),
        TerraformCommandKind.StateMove => BuildStateMove(spec),
        TerraformCommandKind.StateRemove => BuildStateRemove(spec),
        TerraformCommandKind.StatePull => BuildStatePull(),
        TerraformCommandKind.StatePush => BuildStatePush(spec),
        TerraformCommandKind.ForceUnlock => BuildForceUnlock(spec),
        TerraformCommandKind.WorkspaceList => BuildWorkspaceList(),
        TerraformCommandKind.WorkspaceSelect => BuildWorkspace(spec, "select"),
        TerraformCommandKind.WorkspaceNew => BuildWorkspace(spec, "new"),
        TerraformCommandKind.WorkspaceDelete => BuildWorkspace(spec, "delete"),
        TerraformCommandKind.Import => BuildImport(spec),
        TerraformCommandKind.PlanGenerateConfig => BuildPlanGenerateConfig(spec),

        // ---- Phase 8.5: backend-less key-pair generation (self-contained throwaway dir) ----
        TerraformCommandKind.KeyPairGenerateApply => BuildKeyPairGenerateApply(),
        TerraformCommandKind.KeyPairGenerateDestroy => BuildKeyPairGenerateDestroy(),

        // ---- Phase 10: visual resource builder ----
        TerraformCommandKind.ProvidersSchema => BuildProvidersSchema(),

        // ---- Phase 10.5: Terraform-aware code editor ----
        TerraformCommandKind.FormatStdin => BuildFormatStdin(),

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

    // ---- Phase 9: state & inspection tools ----

    private static CommandDefinition BuildStateList() =>
        new("state", ["state", "list"], TerraformRiskLevel.ReadOnly);

    /// <summary>
    /// <c>show -json</c> with no plan file renders the <em>current state</em> as JSON. Read-only, but the
    /// output can contain sensitive values → the caller must not log it (parsed in memory, redacted).
    /// </summary>
    private static CommandDefinition BuildStateShow() =>
        new("show", ["show", "-json"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildOutput(TerraformRunSpec spec)
    {
        var args = new List<string> { "output", "-json" };
        if (!string.IsNullOrWhiteSpace(spec.OutputName))
            args.Add(spec.OutputName);
        return new CommandDefinition("output", args, TerraformRiskLevel.ReadOnly);
    }

    private static CommandDefinition BuildGraph() =>
        new("graph", ["graph"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildStateMove(TerraformRunSpec spec)
    {
        var o = spec.StateMove;
        if (string.IsNullOrWhiteSpace(o.Source) || string.IsNullOrWhiteSpace(o.Destination))
            throw new InvalidOperationException("state mv requires both a source and a destination address.");
        // Rewrites state bindings but touches no real infrastructure.
        return new CommandDefinition("state", ["state", "mv", o.Source, o.Destination], TerraformRiskLevel.StateChanging);
    }

    private static CommandDefinition BuildStateRemove(TerraformRunSpec spec)
    {
        var addresses = spec.StateRemove.Addresses;
        if (addresses.Count == 0 || addresses.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("state rm requires at least one resource address.");
        var args = new List<string> { "state", "rm" };
        args.AddRange(addresses);
        // Forgets resources from state; the real objects are left untouched (orphaned).
        return new CommandDefinition("state", args, TerraformRiskLevel.StateChanging);
    }

    /// <summary>
    /// <c>state pull</c> writes the full remote state (which can contain plaintext secrets) to stdout. It
    /// changes nothing, but the caller must treat the output as sensitive (never logged).
    /// </summary>
    private static CommandDefinition BuildStatePull() =>
        new("state", ["state", "pull"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildStatePush(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.StateFilePath))
            throw new InvalidOperationException("state push requires a source state file path.");
        // Overwrites remote state from a file — the most dangerous state operation.
        return new CommandDefinition("state", ["state", "push", spec.StateFilePath], TerraformRiskLevel.Destructive);
    }

    private static CommandDefinition BuildForceUnlock(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.ForceUnlock.LockId))
            throw new InvalidOperationException("force-unlock requires a lock id.");
        // -force skips Terraform's own interactive prompt; the UI supplies a typed confirmation instead.
        return new CommandDefinition("force-unlock", ["force-unlock", "-force", spec.ForceUnlock.LockId], TerraformRiskLevel.StateChanging);
    }

    private static CommandDefinition BuildWorkspaceList() =>
        new("workspace", ["workspace", "list"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildWorkspace(TerraformRunSpec spec, string verb)
    {
        if (string.IsNullOrWhiteSpace(spec.Workspace.Name))
            throw new InvalidOperationException($"workspace {verb} requires a workspace name.");
        return new CommandDefinition("workspace", ["workspace", verb, spec.Workspace.Name], TerraformRiskLevel.StateChanging);
    }

    private static CommandDefinition BuildImport(TerraformRunSpec spec)
    {
        var o = spec.Import;
        if (string.IsNullOrWhiteSpace(o.Address) || string.IsNullOrWhiteSpace(o.Id))
            throw new InvalidOperationException("import requires a resource address and a real-world id.");
        var args = new List<string> { "import", "-input=false" };
        // Import evaluates configuration, so it needs the environment's variables when one is set.
        if (!string.IsNullOrWhiteSpace(spec.VarFile))
            args.Add($"-var-file={spec.VarFile}");
        args.Add(o.Address);
        args.Add(o.Id);
        return new CommandDefinition("import", args, TerraformRiskLevel.StateChanging);
    }

    /// <summary>
    /// <c>apply -input=false -auto-approve</c> for the throwaway key-generation working directory. No saved
    /// plan is used because the dir has no project state/backend — it exists only to realise a
    /// <c>tls_private_key</c> (+ optional <c>aws_key_pair</c>) so the private key can be captured. See
    /// docs/28-key-pair-management.md.
    /// </summary>
    private static CommandDefinition BuildKeyPairGenerateApply() =>
        new("apply", ["apply", "-input=false", "-auto-approve"], TerraformRiskLevel.StateChanging);

    /// <summary>
    /// <c>destroy -auto-approve</c> for the key-generation working directory — de-registers a cloud-registered
    /// generated key (e.g. <c>aws_key_pair</c>) on delete/rotate. See docs/28-key-pair-management.md.
    /// </summary>
    private static CommandDefinition BuildKeyPairGenerateDestroy() =>
        new("destroy", ["destroy", "-input=false", "-auto-approve"], TerraformRiskLevel.Destructive);

    /// <summary>
    /// <c>providers schema -json</c> — the subcommand is <c>providers</c>. Read-only: it prints the installed
    /// providers' schemas (no infrastructure touched, no secret values). See docs/07-visual-builder.md.
    /// </summary>
    private static CommandDefinition BuildProvidersSchema() =>
        new("providers", ["providers", "schema", "-json"], TerraformRiskLevel.ReadOnly);

    /// <summary>
    /// <c>fmt -</c> — the trailing <c>-</c> makes Terraform read the source from stdin and print the formatted
    /// result to stdout (no file is touched). The buffer travels as <see cref="TerraformRunSpec.StandardInput"/>,
    /// so it never appears in the argument list, the redacted history, or a run log. See docs/05-terraform-engine.md.
    /// </summary>
    private static CommandDefinition BuildFormatStdin() =>
        new("fmt", ["fmt", "-"], TerraformRiskLevel.ReadOnly);

    private static CommandDefinition BuildPlanGenerateConfig(TerraformRunSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.GenerateConfigOutFile))
            throw new InvalidOperationException("Config generation requires a -generate-config-out target.");
        // A plan that writes generated HCL for import{} blocks. It changes no real infrastructure — the
        // generated file is reviewed and then applied like any other config change.
        var args = new List<string> { "plan", "-input=false", $"-generate-config-out={spec.GenerateConfigOutFile}" };
        if (!string.IsNullOrWhiteSpace(spec.VarFile))
            args.Add($"-var-file={spec.VarFile}");
        return new CommandDefinition("plan", args, TerraformRiskLevel.Safe);
    }
}
