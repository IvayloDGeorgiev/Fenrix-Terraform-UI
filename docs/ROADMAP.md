# Roadmap

Twelve implementation phases, an MVP definition, and acceptance criteria. Progress is tracked in [PROGRESS.md](PROGRESS.md).

## Phases

### Phase 1 — Foundation
.NET 10 MAUI Blazor Hybrid solution · DI · navigation shell · **theme + design-token system (dark default + light), base components, and motion vocabulary (modern/animated/futuristic look — [24-visual-design-language.md](24-visual-design-language.md))** · **Help framework shell + command palette + theme toggle ([27-help-and-guidance.md](27-help-and-guidance.md))** · logging · SQLite + EF Core · settings framework · workspace directory creation · basic diagnostics.

### Phase 2 — Project management
Create project · import project · project manifest · default Dev/UAT/Live structure · custom environments · linked external projects · recent projects · file tree · file create/rename/move/delete · filesystem watcher · external-change detection · **file version history capture + recover accidentally deleted files** ([20-pipelines-deployments.md](20-pipelines-deployments.md) uses the version data; [21-file-history-recovery.md](21-file-history-recovery.md)).

### Phase 3 — Terraform execution foundation
Terraform discovery · version detection · process runner · stdout streaming · cancellation · command history · init · format · validate · version · advanced raw command screen · **command-preview component (show the exact command per action)** ([23-command-transparency.md](23-command-transparency.md)).

### Phase 4 — Plans & deployment safety
Saved plans · plan JSON parsing · resource-change display · sensitive-data redaction · plan hashing · configuration hashing · apply exact saved plan · production confirmation · destroy workflow · environment operation locks · drift-only planning.

### Phase 5 — Git core
Repository detection · init & clone · git status · stage/unstage · commit · fetch/pull/push · branch management · commit history · diff viewer · stash · merge · conflict detection.

### Phase 6 — Advanced Git
Interactive rebase · cherry-pick · reset · reflog · blame · tags · submodules · worktrees · Git LFS · conflict editor · partial staging · commit-graph optimisation.

### Phase 7 — Provider integrations
Adapters in order: Generic Git → GitHub → Azure DevOps → Bitbucket → GitLab → AWS CodeCommit → self-hosted configs. Add repository browsing/creation · pull & merge requests · pipeline status · branch-policy display.

### Phase 8 — Cloud connections
Azure CLI login & subscription selection · AWS profiles & SSO · Google ADC & project selection · **global Connections hub** · **per-environment binding (project holds no cloud connection) + creation-time guidance** ([26-connections.md](26-connections.md)) · environment-to-account mappings · connection testing · secure secret references · per-command environment construction.

### Phase 9 — State & inspection tools
State resource browser · state list & show · outputs · dependency graph · import assistant · workspace management · force-unlock workflow · advanced state operations with strong warnings.

### Phase 9.5 — CI/CD Pipelines & Deployments
Deployment board (which version is live per environment) · `Deployment` records (Git commit + state serial/lineage + plan summary) · pipeline definitions with stage gates · promote & rollback · approvals-lite · external-pipeline status via provider adapters. A **read-only deployments board** built from existing plan/apply + Git history can land as early as after Phase 5. See [20-pipelines-deployments.md](20-pipelines-deployments.md).

### Phase 10 — Visual resource builder
Provider-schema cache · provider/resource browser · schema-generated forms · required & optional attributes · HCL preview · new-resource generation · simple existing-resource editing · reusable templates · **form-based authoring for every config-side file type** — providers, versions, variables, outputs, locals, tfvars, backends, data sources, modules ([22-terraform-files-model.md](22-terraform-files-model.md)).

### Phase 10.5 — Terraform-aware code editor
Replace the plain textarea file editor with a professional, offline code editor (recommend CodeMirror 6): line numbers · HCL syntax highlighting · bracket matching · a **Terraform ribbon** — **Format ("Beautify")** via `terraform fmt -` (stdin→stdout), inline **Validate** diagnostics, comment toggle, find/replace, **snippet palette** for HCL blocks, **outline/go-to-symbol**, and `var.`/`local.`/`module.`/`data.` reference helpers. Foundation + fmt/validate are independent of provider/cloud work and can be pulled earlier; schema-aware completion reuses the Phase 10 provider-schema cache. Everything runs through the existing process runner + command preview (no shell strings) and the same atomic-write/file-history path. See [PROGRESS.md](PROGRESS.md#phase-105--terraform-aware-code-editor).

### Phase 11 — Enterprise capability
SQL Server metadata database · shared policies · shared templates · central audit · team configuration · role-based restrictions · remote Fenrix execution agent · approval workflows · organisation-controlled Terraform versions · **central agent-run deployment pipelines** with role-based approvals ([20-pipelines-deployments.md](20-pipelines-deployments.md)).

### Phase 12 — Release preparation
MSIX packaging · code signing · update mechanism · crash recovery · database backup · accessibility review · performance testing · security review · user documentation · example projects · stable release channel.

## MVP definition

The first usable release includes: Windows desktop app · SQLite · project creation · existing-project import · Dev/UAT/Live + custom environments · file management & external-change detection · Terraform executable selection · init/format/validate/plan/apply/destroy · saved-plan visualisation · safe production confirmation · basic embedded terminal · git status/stage/commit/fetch/pull/push/branches · GitHub & Azure DevOps repository connection · Azure/AWS/Google profile selection · settings · logs & command history · dark & light themes.

**Not in MVP:** the full visual resource builder (Phase 10) — it depends on the stable execution, file, and plan foundations. MVP corresponds roughly to Phases 1–5 plus the GitHub/Azure DevOps slices of Phase 7 and the account-selection slices of Phase 8.

## Acceptance criteria

The initial production-ready release succeeds when a DevOps engineer can:

1. Install Fenrix on Windows.
2. Create a Terraform project with Dev, UAT, and Live environments.
3. Import an existing Terraform repository without restructuring it.
4. Add a custom environment.
5. Open and edit Terraform files.
6. See changes made through Windows Explorer or another editor.
7. Run Terraform init and validate from buttons.
8. Generate a saved plan.
9. Review additions, modifications, deletions, and replacements graphically.
10. Apply the exact reviewed plan.
11. Be prevented from applying a stale or modified plan.
12. Select a different cloud account for each environment.
13. View and manage Git changes.
14. Commit, fetch, pull, and push.
15. Create and switch branches.
16. Connect to GitHub or Azure DevOps.
17. Access every installed Terraform command via a typed screen, dynamic builder, or embedded terminal.
18. Review redacted execution history.
19. Operate without plaintext credentials saved in the Fenrix database.
20. Close and reopen the app without losing project registrations or settings.
