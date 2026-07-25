# Progress Tracker

Living record of where the project stands. Update this in the same PR as the work it describes. Tick items as they meet the [Definition of Done](WORKFLOW.md#definition-of-done).

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done

_Last updated: 2026-07-24 — status: **Phase 4 core complete** (saved plan `-out` + `show -json` parsing, three-pane resource-change review with filters and sensitive-value redaction, config/lock/plan hashing with invalidation, apply of the exact saved plan, production typed-confirmation, destroy workflow, per-environment on-disk operation locks, and refresh-only drift plans). Plans + state are version-controlled (git + Fenrix file history) per project. Needs a migration for the new `SavedPlan` table before running — see the Phase 4 note. Phases 2–3 remain complete below._

## Milestone summary

| Phase | Title | Status |
|-------|-------|--------|
| 0 | Design & documentation | **Done** |
| 1 | Foundation | **In progress** |
| 2 | Project management | **Complete** |
| 3 | Terraform execution foundation | **Core complete** |
| 4 | Plans & deployment safety | **Core complete** |
| 5 | Git core | Not started |
| 6 | Advanced Git | Not started |
| 7 | Provider integrations | Not started |
| 8 | Cloud connections | Not started |
| 8.5 | Project secrets & key-pair management | Not started |
| 9 | State & inspection tools | Not started |
| 9.5 | CI/CD Pipelines & Deployments | Not started |
| 10 | Visual resource builder | Not started |
| 11 | Enterprise capability | Not started |
| 12 | Release preparation | Not started |

## Phase 0 — Design & documentation ✅

- [x] Architecture defined ([01](01-architecture.md))
- [x] Solution structure defined ([02](02-solution-structure.md))
- [x] Domain model documented ([03](03-domain-model.md))
- [x] All topic docs (04–19) written
- [x] Workflow, roadmap, progress tracker created
- [x] Foundational ADRs recorded (0001–0003)

## Phase 1 — Foundation  🚧 in progress

> **Structural note:** the MAUI app project stays at the repo root (already wired into
> `.slnx`/`.vs`); the four class libraries were added under `src/` and referenced from it,
> with `src/**` excluded from the app's compile globs. The full rename to
> `src/Fenrix.IaCStudio.App` is deferred to avoid a blind move (no SDK in the authoring
> environment). See [02](02-solution-structure.md).

- [~] Rename/move template into `src/Fenrix.IaCStudio.App` — deferred (app kept at root; see note)
- [x] Add `Domain`, `Application`, `Infrastructure`, `Contracts` projects with reference rules
- [x] DI composition root in `MauiProgram` (`AddFenrixApplication` + `AddFenrixInfrastructure`)
- [x] Navigation shell (left rail, top bar, status bar) ([13](13-ui-design.md))
- [x] Theme + design-token system (dark-first + light; high-contrast tokens pending) ([24](24-visual-design-language.md))
- [x] Base components + motion vocabulary (rise animation, reduced-motion aware) ([24](24-visual-design-language.md))
- [~] Help framework: Help tab + theme toggle done; searchable content, contextual "?", command-explanation toggle, command palette pending ([27](27-help-and-guidance.md))
- [x] Logging wired (`Microsoft.Extensions.Logging`, `AddDebug`) ([15](15-logging-auditing.md))
- [x] EF Core + SQLite (schema via `EnsureCreated`; switch to migrations next) ([12](12-database-design.md))
- [x] Settings framework with scope resolution (`SettingsService`) ([14](14-settings.md))
- [x] Workspace directory creation + `%LOCALAPPDATA%` fallback (`WorkspacePaths`) ([03](03-domain-model.md))
- [ ] Basic diagnostics export

_Phase 1 build the user should see in Visual Studio: a themed shell (rail + top bar + status bar),
dark/light toggle that persists (dark default), Dashboard/Projects/Connections/Activity/Templates/Help/Settings
pages, SQLite DB + workspace tree created on first launch._

## Phase 2 — Project management  ✅ complete

> **Build note.** Solution builds in Visual Studio (the initial `CS0542` name-clash on `Projects.razor`
> is fixed). This phase switches EF Core to **migrations**: on first run the app still uses
> `EnsureCreated` until a migration exists, then it auto-switches to `Migrate`. Generate the initial
> migration once (design-time factory added):
>
> ```
> dotnet ef migrations add InitialCreate -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure
> ```
>
> A dev `fenrix.db` created via `EnsureCreated` no longer needs deleting — `AppInitializer` adopts it
> automatically (creates missing model tables, stamps migrations as applied, keeps data). See
> [12](12-database-design.md).

- [x] Create project (recommended structure) ([03](03-domain-model.md)) — `ProjectService` + `ProjectScaffolder`
- [x] Import existing project wizard (no restructuring) — `ProjectImportScanner` + `ImportProjectDialog`
- [x] Project manifest read/write (`.fenrix/project-manifest.json`) — `ProjectManifestStore`
- [x] Default Dev/UAT/Live + custom environments — new-project dialog env editor
- [x] Linked external projects — `InfrastructureProject.IsLinked`, registered in place
- [x] Recent projects — `LastOpenedAt` + `GetRecentAsync`
- [x] File tree + create/rename/move/delete (Recycle Bin) — `FileTreeService` (+ `RecycleBin`); UI exposes create/rename/delete (move via rename), drag-move UI is future
- [x] `FileSystemWatcher` + reconciliation + change journal ([04](04-filesystem-sync.md)) — `ProjectFileSynchronizer` + `ChangeJournal`
- [x] File version history capture (create/update snapshots, dedup, compression) ([21](21-file-history-recovery.md)) — `FileHistoryStore` (GZip + SHA-256 dedup)
- [x] Recover accidentally deleted files (in-app delete disabled by default; external deletes recoverable) — Recoverable-items panel
- [x] `IFileHistoryStore` works on both SQLite and SQL Server — provider-neutral EF only (SQL Server not yet exercised)
- [ ] _Follow-ups:_ retention/pruning job, file-history diff view, drag-to-move in tree, watcher-exclusions settings UI, real `git init` (needs Phase 3 process runner)

## Phase 3 — Terraform execution foundation  ✅ core complete

> **Build/migration note.** Phase 3 adds an explicit EF configuration for the existing `CommandRun`
> entity (indexes on `StartedAt`, `(ProjectId, StartedAt)`, `EnvironmentId`; column lengths). After
> pulling these changes, generate a migration in Visual Studio before running:
> if the `InitialCreate` migration hasn't been made yet, `InitialCreate` now captures everything; if it
> already exists, add a follow-up (e.g. `AddCommandRunHistory`). Terraform must be on `PATH` or set in
> Settings (`terraform.executable`) for discovery to resolve a binary.
>
> **Warnings (NU1903 security pins in `Directory.Build.props`):** two transitive packages are flagged
> high-severity and pinned to patched versions (each needs a `dotnet restore`): `System.Security.Cryptography.Xml`
> → `10.0.10` (net10 stack resolved a vulnerable `[10.0.0, 10.0.9]` build — CVE-2026-50525/-50527/-47304;
> supersedes the earlier `9.0.15` pin), and `SQLitePCLRaw.lib.e_sqlite3` → `3.50.3` (EF Core SQLite ships
> the vulnerable `2.1.11` with SQLite < 3.50.2 — CVE-2025-6965; native lib overridden, managed
> core/provider stay at 2.1.11). Remove each once the upstream packages reference patched versions
> (the SQLite fix is slated for EF Core 11).

- [x] Terraform discovery + version detection + constraint enforcement ([05](05-terraform-engine.md)) — `TerraformVersion`/`TerraformVersionConstraint` (full `required_version` grammar incl. `~>`, prerelease precedence; validated against 16 cases), `TerraformInstallation.SatisfiesConstraint`, `TerraformDiscovery` (configured path → PATH → `version -json`)
- [x] Process runner (`ArgumentList`, cancellation, tree-kill, structured events) — `IProcessRunner`/`ProcessRunner` (`UseShellExecute=false`, redirected streams, `Kill(entireProcessTree:true)`, `IProgress<ProcessOutputEvent>`)
- [x] stdout/stderr streaming to UI — `OutputConsole` (append-only, auto-scroll) fed live via `Progress<T>`
- [x] Command history (redacted) — `ICommandHistoryStore`/`EfCommandHistoryStore` (persists `CommandRun`; raw output to `Logs/terraform/<runId>.log`; args redacted via `ArgumentRedactor`)
- [x] Typed screens: init, format, validate, version — `TerraformRun` page at `/projects/{id}/terraform` (env selector, per-command options, `validate -json` → structured diagnostics)
- [x] Command-preview component — show exact command per action, live-updating, redacted, copyable ([23](23-command-transparency.md)) — `CommandPreviewPanel` + `CommandPreviewBuilder`/`TerraformCommandCatalog` (preview and execution share one `ArgumentList`, so they can't diverge)
- [ ] _Deferred to a Phase 3 follow-up:_ dynamic raw command builder (`terraform -help` discovery) + embedded ConPTY terminal
- [ ] _Follow-ups:_ Settings UI for the Terraform executable path + a discovered-versions picker; history retention/pruning; cloud-credential env injection (Phase 8) so previews show credential chips
- [ ] _Follow-up — Terraform tab (binary manager):_ show the current install (version/path/source); one-click **install Terraform for Windows** (download from `releases.hashicorp.com`, verify SHA256SUMS + GPG signature, unzip into `WorkspacePaths.ToolsDirectory`, set `terraform.executable`); **check-for-updates / update**; optional **version picker** to install/switch a specific version (multi-version, tfenv-style). See [05](05-terraform-engine.md#installation--version-management-planned--terraform-tab)

## Phase 4 — Plans & deployment safety  ✅ core complete

> **Build/migration note.** Phase 4 adds a new `SavedPlan` entity + `SavedPlanConfiguration` (table
> `SavedPlans`). After pulling these changes, generate a migration in Visual Studio before running:
> `dotnet ef migrations add AddSavedPlans -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure`.
> No other schema changed. Plans are written to `plans/<env>/` and env locks to `.fenrix/locks/` inside
> each project; both, plus state and the provider lock, are now git-tracked (`.gitignore` updated in the
> scaffolder). `.tfplan`/`.tfstate` were added to the file-history versioned extensions. **Security:** plan
> and state files hold sensitive values in plaintext by design — keep project repositories private.
>
> Parsing logic (`PlanJsonParser`, `ApplyJsonParser`) and integrity hashing were validated against real
> `show -json` / `apply -json` fixtures via a reference cross-check (25 assertions, all passing) since MAUI
> can't be compiled in the authoring environment.

- [x] Saved plan (`-out`) + `show -json` parsing ([06](06-plan-apply-safety.md)) — `TerraformPlanService` runs `plan -out` then `show -json`; `PlanJsonParser` parses it in memory (never persisted raw)
- [x] Resource-change display (3-pane) + filters — `PlanReviewPanel` (summary cards, action + text filters, list | detail | before/after) at `/projects/{id}/plan`
- [x] Sensitive-data redaction ([11](11-secrets.md)) — `before_sensitive`/`after_sensitive` → `••••`, `after_unknown` → "(known after apply)"; `-json` outputs never written to logs
- [x] Plan + configuration + lock hashing; invalidation — `PlanIntegrity` (combined config hash + `.terraform.lock.hcl` hash + plan-file hash); preflight re-hashes and marks stale plans un-appliable
- [x] Apply exact saved plan — `TerraformApplyService` runs `apply -input=false -json <plan>`; no var-file; re-verifies plan hash immediately before executing
- [x] Production confirmation (type env name) — preflight `RequiresTypedConfirmation`; apply refused unless the typed value equals the environment name
- [x] Destroy workflow — `plan -destroy -out` → same review (all deletes) → apply the saved destroy plan (saved-plan-only, ADR-0003)
- [x] Per-environment operation locks — `IEnvironmentLockService`/`FileEnvironmentLockService` (on-disk `.fenrix/locks/<env>.lock`, PID-based staleness + force-release); read-only `show` doesn't lock
- [x] Drift-only (refresh-only) planning — `plan -refresh-only -out`; drift surfaced in the same review UI
- [ ] _Deferred to Phase 5:_ Git provenance in plan metadata (commit/branch/uncommitted warnings) — `SavedPlan` has the nullable fields; populated once the Git engine lands
- [ ] _Follow-ups:_ structured `apply -json` progress covers per-resource status/timing but not the dependency-ordering annotations from doc 25's worked example; config hashing covers the working-dir subtree + `modules/` only (modules referenced from elsewhere aren't tracked); deployment recording is Phase 9.5

## Phase 5 — Git core  ✅ core complete

> **Build/migration note.** Phase 5 adds **no new database schema** — the `SavedPlan` Git-provenance columns
> (`GitCommitSha`/`GitBranch`/`GitTreeDirty`) already shipped in the Phase 4 `AddSavedPlans` migration, and
> `CommandRun` already carries a `Tool` discriminator (now written as `git`). So after pulling these changes
> you can build and run without generating a migration. Git must be on `PATH` (or set `git.executable` in
> Settings). Redacted Git history is recorded like Terraform; raw git output goes to `Logs/git/<runId>.log`.
> **Remote posture this phase is local-only:** fetch/pull/push run non-interactively (`GIT_TERMINAL_PROMPT=0`)
> against already-configured credentials (Git Credential Manager) and fail fast instead of prompting — no
> in-app credential UX yet.
>
> The five parsers (status `--porcelain=v2 -z`, `log` NUL+0x1e records, unified diff, branch, stash) were
> cross-checked against **real `git` 2.34.1 output** via a reference port (38 assertions over temp repos:
> rename token-consumption, unmerged `u` records, ahead/behind, merge parents, rename diffs, line numbering)
> since MAUI can't be compiled in the authoring environment.

- [x] Repository detection · init · clone ([08](08-git-engine.md)) — `GitService.DetectAsync`/`ResolveContextAsync` (`rev-parse --show-toplevel`); `git init` runs **automatically on project creation** (`GitRepositoryInitializer` → `ProjectService.CreateAsync`, sets `RepositoryRootPath`) and on demand for existing projects via the **Initialise repository** button; `CloneAsync` (streamed) with a VS-style clone dialog — URL + location/name for a **new project** (auto-imported via the scanner, name-collision validated against existing projects) or **existing project** + environment (clone into a subfolder)
- [x] Status (`--porcelain=v2 -z`) parsing — `GitStatusParser` (ordinary/rename/unmerged/untracked/ignored records; rename's original path is the following NUL token; branch/upstream/ahead-behind headers)
- [x] Stage/unstage · commit · fetch/pull/push — `GitService` (`add`/`reset`/`restore`, `commit` +stage-all/amend/sign-off, `fetch --all --prune`/`pull`/`push`); Changes tab groups staged/unstaged/untracked/conflicts with per-file stage/unstage/discard
- [x] Git command preview on every action ([23](23-command-transparency.md)) — `GitCommandCatalog` is the single `ArgumentList` source (preview == execution); `GitCommandPreviewBuilder` builds the redacted `CommandPreview` (reuses the shared `CommandPreviewPanel`); remote-URL credentials redacted (`GitUrlRedactor`); commit/branch/clone dialogs and every destructive confirm show the exact command
- [x] Branch management · history · diff viewer — `GitBranchParser`/`GitLogParser`/`GitDiffParser`; Branches tab (create/checkout/rename via catalog, merge, delete with confirm, ahead/behind, remote-tracking checkout), History tab (log + copy-hash + per-commit diff), read-only unified `GitDiffView`
- [x] Stash · merge · conflict detection — stash push (incl. untracked)/apply/pop/drop; `MergeAsync` detects a non-zero merge, re-reads status for conflicted paths, surfaces them and offers `merge --abort` (the interactive conflict **editor** is Phase 6)
- [x] Phase 4 deferred — Git provenance in plan metadata — `TerraformPlanService` captures commit/branch/dirty into `SavedPlan`; `TerraformApplyService` preflight adds non-blocking **branch-changed**, **HEAD-moved**, and **uncommitted-changes** warnings
- [x] Scalable project selection — shared `ProjectCard`, a searchable **`ProjectPickerDialog`** (opened from the Source control "Select a project" button) and a reworked **Projects** page (search by name/path, status active/archived/all, location in-workspace/linked, sort recent/name/created) — both use Blazor `Virtualize` so they stay fast with thousands of projects (client/tag grouping deferred to Phase 8)
- [ ] _Follow-ups (Phase 6+ / noted):_ remote credential UX + auth-failure guidance; per-environment sparse clone (currently a full clone into a subfolder); partial/line staging + conflict editor; tags/reset/rebase/reflog/blame; commit templates; history search/paging; process runner still lives under the Terraform contracts namespace (shared `ProcessStartRequest`)

## Phase 6 — Advanced Git

- [ ] Interactive rebase · cherry-pick · reset · reflog · blame
- [ ] Tags · submodules · worktrees · Git LFS
- [ ] Conflict editor · partial staging · commit-graph optimisation

## Phase 7 — Provider integrations

- [ ] Generic Git · GitHub · Azure DevOps ([09](09-provider-integrations.md))
- [ ] Bitbucket · GitLab · AWS CodeCommit · self-hosted
- [ ] Repo browse/create · PR/MR · pipeline status · branch policies

## Phase 8 — Cloud connections

- [ ] Global Connections hub (library, search, filter, group by client/provider) ([26](26-connections.md))
- [ ] Scales to hundreds/thousands of connections (virtualized list, indexed search, pagination)
- [ ] Client/group organization + tags + favorites
- [ ] Per-environment cloud connection binding (project holds no cloud connection) + creation-time guidance/validation
- [ ] Azure login + subscription selection ([10](10-cloud-integrations.md))
- [ ] AWS profiles + SSO
- [ ] Google ADC + project selection
- [ ] Env-to-account mappings · connection testing · secret references · per-command env

## Phase 9 — State & inspection tools

- [ ] State browser · list/show · outputs · dependency graph
- [ ] Import assistant · workspace management · force-unlock · advanced state ops

## Phase 8.5 — Project secrets & key-pair management

- [ ] Per-project **SSH/EC2 key-pair** management: import existing keys into a secure app folder ([28](28-key-pair-management.md))
- [ ] **Generate** key pairs via Terraform on the backend (`tls_private_key` + `aws_key_pair`), auto-capture the sensitive output into the secure store — no AWS-console round-trip
- [ ] Encrypted-at-rest (DPAPI) private keys under `Data\keys\<projectId>\`; DB holds only metadata + a `SecretReference` ([11](11-secrets.md))
- [ ] Keys section inside the project (view fingerprint/public key/source; copy public key or secure path; rotate/delete; gated+audited private-key export)
- [ ] Reference-picker so `connection`/`provisioner`/`aws_key_pair` blocks point at a managed key
- [ ] _Stretch:_ "Connect" (SSH to instance/bastion using a managed key); tfvars secret references; policy/cost/drift add-ons (see [28](28-key-pair-management.md))

## Phase 9.5 — CI/CD Pipelines & Deployments

- [ ] `ProjectVersion` model (per-project, Git-anchored, semver labels) ([20](20-pipelines-deployments.md))
- [ ] `Deployment` records (version + state serial/lineage + plan summary)
- [ ] Independent version-per-environment (v1/Live, v1.5/UAT, v2/Dev)
- [ ] Version × environment matrix view
- [ ] Deploy one version to many/all environments (governed fan-out)
- [ ] Read-only deployments board (from plan/apply + Git history)
- [ ] Pipeline definitions + ordered stages
- [ ] Stage gates (approval, branch, clean tree, production typed-confirm)
- [ ] One-click governed deploy (plan → gates → apply saved plan)
- [ ] Promote & rollback
- [ ] External-pipeline status on the board (provider adapters)

## Phase 10 — Visual resource builder

- [ ] Provider-schema cache ([07](07-visual-builder.md))
- [ ] Provider/resource browser · schema forms · HCL preview · generation · templates
- [ ] Form authoring for all config-side files (providers, versions, variables, outputs, locals, tfvars, backends, data, modules) ([22](22-terraform-files-model.md))

## Phase 11 — Enterprise capability

- [ ] SQL Server metadata · shared policies/templates · central audit
- [ ] Role-based restrictions · Fenrix Agent · approval workflows · org-controlled versions

## Phase 12 — Release preparation

- [ ] MSIX packaging · code signing · update mechanism ([18](18-packaging-deployment.md))
- [ ] UI polish + animation performance + accessibility (reduced-motion, contrast) pass ([24](24-visual-design-language.md))
- [ ] Crash recovery · DB backup · accessibility · performance · security review
- [ ] User docs · example projects · stable channel

## Acceptance criteria (see [ROADMAP.md](ROADMAP.md#acceptance-criteria))

- [ ] 1–20 all passing → initial production-ready release.

## Decision log

| Date | Decision | Link |
|------|----------|------|
| 2026-07-23 | Drive official CLIs; no reimplementation | [ADR-0001](adr/0001-drive-official-clis.md) |
| 2026-07-23 | Files are source of truth; DB is index | [ADR-0002](adr/0002-files-as-source-of-truth.md) |
| 2026-07-23 | Apply only the exact reviewed saved plan | [ADR-0003](adr/0003-saved-plan-only-apply.md) |
| 2026-07-23 | DB-backed file version history for recovery | [ADR-0004](adr/0004-db-file-version-history.md) |
| 2026-07-23 | Switch DB schema from EnsureCreated to EF migrations (with fallback) | [12](12-database-design.md) |
| 2026-07-24 | Store DateTimeOffset as sortable binary on SQLite (SQLite can't ORDER BY/compare it) | [12](12-database-design.md) |
| 2026-07-24 | Plans, state, and the provider lock live inside each project (`plans/<env>/`) and are version-controlled (git + Fenrix file history); repos must be private (plaintext secrets) | [06](06-plan-apply-safety.md) |
| 2026-07-24 | Per-environment operation lock is an on-disk lock file in `.fenrix/locks/` (crash-safe, PID-staleness, force-releasable) | [06](06-plan-apply-safety.md) |
| 2026-07-24 | Keep every saved plan as its own file so any reviewed plan stays exactly applyable (ADR-0003); apply uses `apply -json` for structured per-resource progress | [06](06-plan-apply-safety.md) |
| 2026-07-24 | `AppInitializer` adopts a legacy `EnsureCreated` DB (create missing tables + stamp migration history) instead of requiring a reset — upgrades never lose data | [12](12-database-design.md) |
