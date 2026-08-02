namespace Fenrix.IaCStudio.Application.Settings;

/// <summary>Well-known setting keys. See docs/14-settings.md.</summary>
public static class FenrixSettingKeys
{
    // Appearance
    public const string Theme = "appearance.theme";                 // light | dark | system
    public const string Density = "appearance.density";             // compact | comfortable
    public const string ReducedMotion = "appearance.reducedMotion"; // true | false

    // General
    public const string ReopenLastProject = "general.reopenLastProject";
    public const string DefaultProjectsDirectory = "general.defaultProjectsDirectory";

    // Files & history (Phase 2). See docs/04-filesystem-sync.md, docs/21-file-history-recovery.md.
    public const string FileWatcherExclusions = "files.watcherExclusions";       // comma-separated dir names
    public const string ReconcileIntervalSeconds = "files.reconcileIntervalSeconds";
    public const string AllowInAppDelete = "security.allowInAppDelete";          // true | false (default false)
    public const string HistoryRetentionDays = "security.historyRetentionDays";  // int, 0 = keep all

    /// <summary>Gate for revealing/exporting managed private keys. Off by default. See docs/28-key-pair-management.md.</summary>
    public const string AllowPrivateKeyExport = "security.allowPrivateKeyExport"; // true | false (default false)

    // Terraform
    public const string TerraformExecutable = "terraform.executable";
    public const string TerraformDefaultParallelism = "terraform.defaultParallelism";

    // Terraform-aware code editor (Phase 10.5). See docs/13-ui-design.md, docs/14-settings.md.
    /// <summary>Run <c>terraform fmt</c> on the buffer automatically before each save. Off by default.</summary>
    public const string EditorFormatOnSave = "editor.formatOnSave";   // true | false (default false)
    /// <summary>Editor font size in px (default 13).</summary>
    public const string EditorFontSize = "editor.fontSize";           // int
    /// <summary>Soft-wrap long lines in the editor (default false).</summary>
    public const string EditorWordWrap = "editor.wordWrap";           // true | false (default false)

    // Checks — static analysis & cost (Phase 13). Per-tool executable overrides; resolution falls back to PATH.
    // The Infracost API key is NOT a setting — it lives in the secret store. See docs/34-checks.md.
    public const string TfLintExecutable = "checks.tflint.executable";
    public const string TfsecExecutable = "checks.tfsec.executable";
    public const string TrivyExecutable = "checks.trivy.executable";
    public const string InfracostExecutable = "checks.infracost.executable";

    // Git
    public const string GitExecutable = "git.executable";

    // Workspace
    public const string DataRootOverride = "workspace.dataRoot";
}
