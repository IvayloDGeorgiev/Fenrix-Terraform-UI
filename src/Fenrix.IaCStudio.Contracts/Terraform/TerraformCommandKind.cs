namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// The typed commands Fenrix can run. Version/Init/Format/Validate shipped in Phase 3; Plan/Apply/Show
/// are the Phase 4 plan-and-apply-safety additions. Destroy and refresh-only are not separate kinds —
/// they are a <see cref="Plan"/> with <see cref="PlanOptions.Destroy"/> / <see cref="PlanOptions.RefreshOnly"/>
/// set, followed by an <see cref="Apply"/> of the saved plan. See docs/05-terraform-engine.md,
/// docs/06-plan-apply-safety.md.
/// </summary>
public enum TerraformCommandKind
{
    Version = 0,
    Init = 1,
    Format = 2,
    Validate = 3,

    /// <summary><c>terraform plan -out</c> (optionally <c>-destroy</c> / <c>-refresh-only</c>).</summary>
    Plan = 4,

    /// <summary><c>terraform apply</c> of an exact saved plan file.</summary>
    Apply = 5,

    /// <summary><c>terraform show -json &lt;plan&gt;</c> — read-only conversion of a saved plan for review.</summary>
    Show = 6,

    // ---- Phase 9: state & inspection tools (docs/05, docs/25 "Read-only inspection", docs/22) ----

    /// <summary><c>terraform state list</c> — read-only enumeration of tracked resource addresses.</summary>
    StateList = 7,

    /// <summary>
    /// <c>terraform show -json</c> (no plan file) — read-only conversion of the <em>current state</em> to
    /// JSON for the redacted state browser. Distinct from <see cref="Show"/>, which converts a saved plan.
    /// </summary>
    StateShow = 8,

    /// <summary><c>terraform output -json</c> — read-only root output values (sensitive redacted).</summary>
    Output = 9,

    /// <summary><c>terraform graph</c> — read-only dependency graph in DOT, rendered visually.</summary>
    Graph = 10,

    /// <summary><c>terraform state mv</c> — moves/renames a resource in state (state-changing).</summary>
    StateMove = 11,

    /// <summary><c>terraform state rm</c> — removes resources from state without destroying them (state-changing).</summary>
    StateRemove = 12,

    /// <summary><c>terraform state pull</c> — reads remote state to stdout (read-only, but sensitive → never logged).</summary>
    StatePull = 13,

    /// <summary><c>terraform state push</c> — overwrites remote state from a file (state-changing, dangerous).</summary>
    StatePush = 14,

    /// <summary><c>terraform force-unlock</c> — releases a stuck state lock by id (state-changing).</summary>
    ForceUnlock = 15,

    /// <summary><c>terraform workspace list</c> — read-only enumeration of workspaces (current marked <c>*</c>).</summary>
    WorkspaceList = 16,

    /// <summary><c>terraform workspace select</c> — switches the active workspace (state-changing).</summary>
    WorkspaceSelect = 17,

    /// <summary><c>terraform workspace new</c> — creates a workspace (state-changing).</summary>
    WorkspaceNew = 18,

    /// <summary><c>terraform workspace delete</c> — deletes a workspace (state-changing).</summary>
    WorkspaceDelete = 19,

    /// <summary><c>terraform import ADDRESS ID</c> — imports an existing object into state (state-changing).</summary>
    Import = 20,

    /// <summary>
    /// <c>terraform plan -generate-config-out=&lt;file&gt;</c> — the config-generation half of the Terraform
    /// 1.5+ <c>import{}</c> workflow: with import blocks present in config, this writes generated HCL for the
    /// imported resources. No state is changed (it is a plan). See docs/22-terraform-files-model.md.
    /// </summary>
    PlanGenerateConfig = 21,

    /// <summary>
    /// <c>terraform apply -auto-approve</c> in a <em>self-contained, throwaway</em> working directory used
    /// only for backend-less key-pair generation (docs/28-key-pair-management.md). This is deliberately
    /// outside the saved-plan-only-apply rule (docs/06), which governs applies to a project's real state: here
    /// there is no project state or backend — the dir is created, applied, its sensitive output captured into
    /// the secure store, and (for local keys) discarded. Never used for project environments.
    /// </summary>
    KeyPairGenerateApply = 22,

    /// <summary>
    /// <c>terraform destroy -auto-approve</c> in the self-contained working directory kept for a
    /// cloud-registered generated key — used to de-register the cloud object (e.g. <c>aws_key_pair</c>) when
    /// the key is deleted or rotated. See docs/28-key-pair-management.md.
    /// </summary>
    KeyPairGenerateDestroy = 23,

    // ---- Phase 10: visual resource builder (docs/07-visual-builder.md, docs/22-terraform-files-model.md) ----

    /// <summary>
    /// <c>terraform providers schema -json</c> — read-only export of the machine-readable provider/resource/
    /// data-source schemas backing the visual builder's schema-driven forms. The output is large but carries
    /// no secrets (it describes attribute shapes, not values); it is cached offline under
    /// <c>Cache/terraform-schemas</c> and never written to a run log (parsed in memory to keep logs lean).
    /// Requires the providers to be installed (i.e. <c>init</c> has run). See docs/07-visual-builder.md.
    /// </summary>
    ProvidersSchema = 24,

    // ---- Phase 10.5: Terraform-aware code editor (docs/05-terraform-engine.md, docs/13-ui-design.md) ----

    /// <summary>
    /// <c>terraform fmt -</c> — reads an HCL buffer from <em>stdin</em> and writes the canonically-formatted
    /// result to <em>stdout</em>, touching no files. Backs the editor's "Beautify" action on the live buffer
    /// (the formatted text replaces the buffer; the on-disk save still goes through the atomic-write +
    /// file-history path). Read-only with respect to the filesystem and infrastructure. The buffer is passed
    /// as <see cref="TerraformRunSpec.StandardInput"/> and is never written to a run log. See docs/05-terraform-engine.md.
    /// </summary>
    FormatStdin = 25,

    // ---- Phase 12: dynamic command builder (docs/05-terraform-engine.md, docs/23-command-transparency.md) ----

    /// <summary>
    /// A dynamically-built command whose argument list comes from <see cref="TerraformRunSpec.CustomArguments"/>
    /// (subcommand first). Backs the "-help"-driven command builder so <em>every</em> installed Terraform
    /// command is reachable. It still flows through the one <see cref="TerraformRunSpec"/> → catalog → runner
    /// spine (ArgumentList only, preview == execution, redacted history). The builder UI refuses to construct a
    /// Custom run for a mutating command (apply/destroy/import/state mv|rm|push/force-unlock/workspace
    /// new|delete|select/taint/untaint/login/logout), redirecting to that command's dedicated safe screen so the
    /// saved-plan-only-apply rule (ADR-0003) and per-environment locking are never bypassed. Risk is classified
    /// from the subcommand by <c>TerraformCommandClassifier</c>.
    /// </summary>
    Custom = 26
}
