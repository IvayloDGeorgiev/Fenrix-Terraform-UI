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

- [x] Inspection & local history rewriting ([08](08-git-engine.md)) — **reflog** (`GitReflogParser`, Reflog tab, "reset to here" recovery), **blame** (`git blame --line-porcelain` → `GitBlameParser`, `GitBlameView` gutter, Diff/Blame toggle in the right pane), **reset** soft/mixed/mixed-vs-hard (Reset dialog with mode radios; **hard → confirm + reflog-recovery hint**), **cherry-pick** + **revert** (`--no-edit`) from the History tab with continue/abort/skip, and **commit-graph** optimisation (`commit-graph write --reachable --changed-paths`, History "Optimise" button). A generalised **sequencer banner** (`GitSequencerState` from git-dir marker files) drives continue/abort/skip for merge/cherry-pick/revert/rebase
- [x] Tags · worktrees · submodules · Git LFS — **tags** lightweight+annotated (`GitTagParser` via `for-each-ref`; Tags tab: create dialog, push one/all, delete **local** and **remote** with confirm); **worktrees** list/add/remove(confirm)/prune (`GitWorktreeParser --porcelain`); **submodules** status/update/sync (`GitSubmoduleParser`, state markers); **Git LFS** indicators (`GitLfsInfo` + `GitLfsParser`, per-file "LFS" badge in the Changes view)
- [x] Conflict editor · partial staging · interactive rebase · commit-graph — full **3-way conflict editor** (`ConflictEditor`: base/ours/theirs panes read from index stages `:1:/:2:/:3:`, editable merged result with marker-guard, take-ours/theirs, stage-on-resolve); **partial/line staging** (`GitPatchBuilder` reconstructs a minimal patch of selected lines → `git apply --cached [--reverse]`; checkboxes in `GitDiffView`); **interactive rebase** full UI todo builder (`RebaseDialog`: reorder + pick/reword/squash/fixup/edit/drop, `+autosquash`) driven non-interactively via `GIT_SEQUENCE_EDITOR`/`GIT_EDITOR` (`cp` sequence editor, reword via `exec git commit --amend -F`)
- [x] Safety posture preserved — `reset --hard`, `clean`/discard, force push, branch delete, remote-tag delete, worktree remove all gate on confirmation; `--force-with-lease` preferred; every action shows the exact `git …` via the shared `GitCommandCatalog`/`CommandPreviewPanel`. **No new DB migration.**
- [x] Verified — new parsers + the patch builder + the rebase/conflict mechanisms cross-checked against **real git 2.34.1** (Python reference port, 44 assertions: reflog/blame/tag/worktree/submodule parsing; `git apply --cached` forward + reverse partial staging; `GIT_SEQUENCE_EDITOR`-driven reword/fixup/squash/drop; `:n:` stage reads + take-side resolve). MAUI itself isn't compiled in the authoring environment.
- [ ] _Deferred (still open):_ remote credential UX + auth-failure guidance; per-environment sparse clone; submodule **add** (catalog kind reserved, no UI); rebase **edit** pauses to the sequencer banner (no inline amend UI yet); process runner still under the Terraform contracts namespace (`ProcessStartRequest`)

## Phase 7 — Provider integrations

_✅ Complete. Full provider stack — abstraction, six adapters, secret backbone, Connections hub, SourceControl provider panel, repo-connection binding, both Git follow-ups, and fixture/redaction verification. Cloud sign-in stays in Phase 8._

- [x] `IRepositoryProvider` abstraction + `ProviderCapabilities` flags + `IRepositoryProviderFactory` (fall-back-to-Generic-Git) ([09](09-provider-integrations.md))
- [x] Generic Git · GitHub · Azure DevOps · Bitbucket · GitLab · AWS CodeCommit adapters (raw `HttpClient`, `ProviderResult<T>` typed errors)
      — GitHub/GitLab/Azure DevOps/Bitbucket implement repo browse/create · PR/MR · pipeline status · branch policies; CodeCommit is a minimal adapter (IAM/SigV4 REST deferred to Phase 8)
- [x] Self-hosted / self-managed base-URL support (GitHub Enterprise, GitLab self-managed, Azure DevOps Server)
- [x] Secret backbone pulled forward from Phase 8: `ISecretStore` → Windows Credential Manager (P/Invoke); only a `SecretReference` in SQLite
- [x] Global **Connections hub** (two sections: Git providers + cloud accounts): search, favorites, archive, test, add/edit; virtualized list; `IConnectionService` (EF CRUD + usage-guard)
- [x] **SourceControl provider panel** (`ProviderPanel`, new Provider tab): repo browse/create · PR/MR list+create · pipeline status · branch policy, gated by each adapter's capability flags, with `ProviderResult<T>` guidance surfaced
- [x] **Repo-connection binding** on a project (`InfrastructureProject.RepositoryConnectionId` via `IProjectService.SetRepositoryConnectionAsync`; bind/change from the Provider tab); host repo id derived from the Git remote via `RepoUrlParser` + `IRepositoryHostService`
- [x] Git follow-ups: **remote credential UX + auth-failure guidance** (`GitRemoteError` enriches failed remote ops with next steps) and **per-environment sparse clone** (`--filter=blob:none --sparse` + `sparse-checkout set`, path field in the clone dialog)
- [x] Provider JSON fixtures + contract cross-check (`tests/provider-fixtures/`, 106/106 key-presence + 11/11 `RepoUrlParser` via Python reference port); token-leakage/redaction checks (tokens confined to the secret-store path, never logged/persisted)
- [x] Full cloud-connection sign-in (Azure login, AWS SSO, Google ADC) + per-environment binding → **done in Phase 8**

## Phase 8 — Cloud connections

_✅ Core complete. Cloud provider abstraction + Azure/AWS/Google adapters (official CLIs via the shared process runner), per-environment binding (picker + apply-one-to-all + warning/block), connection testing, and the bound connection wired into Terraform execution (composed env + identity chip). **No new migration** — `ProjectEnvironment.CloudConnectionId`, the secret backbone, and `CloudConnection.LastStatus/LastTestedAt/Client/MetadataJson` all already exist._

- [x] Global Connections hub (library, search, filter, favorites, archive) — delivered in Phase 7; cloud test + status now wired ([26](26-connections.md))
- [x] Scales to hundreds/thousands of connections (virtualized list, indexed search, pagination) — Phase 7 groundwork reused by the per-environment picker (searchable, client-scoped, paged)
- [x] Client/group organization + tags + favorites — Phase 7 groundwork; the per-environment picker pre-scopes to the project's client
- [x] `ICloudConnectionProvider` (+ `ICloudConnectionProviderFactory`, `ICloudEnvironmentComposer`) mirroring `IRepositoryProvider`: `TestAsync` + `GetAvailableScopesAsync` + `BuildEnvironmentAsync` (process-scoped creds composed at execution time, secret resolved just-in-time, discarded after) ([10](10-cloud-integrations.md))
- [x] Azure adapter (az CLI login + subscription selection; service-principal `ARM_*` when a client id + stored secret exist)
- [x] AWS adapter (named profile / IAM Identity Center SSO; `AWS_PROFILE`/`AWS_REGION`; `sts get-caller-identity` test; profile discovery)
- [x] Google adapter (ADC + project selection; `GOOGLE_PROJECT`/`GOOGLE_CLOUD_PROJECT`, optional SA-file path; active-account test; project discovery)
- [x] Per-environment cloud connection binding (project holds no cloud connection): searchable/client-scoped picker, "apply one to all" at project creation, persistent warning badge + **state-changing ops blocked** when unbound (authentication-required)
- [x] Connection testing (records `LastTestedAt`/`LastStatus`) — on-demand from the hub and from the connection dialog (Save & test); never a blocking sweep
- [x] Bound connection wired into execution: plan/apply/init compose the env from the environment's connection, the account identity shows in the command-preview context chips, secrets are never placed in args/history (redacted env chips) ([25](25-execution-lifecycle.md))
- [x] Cloud CLI shim handling (`CloudCli`): PATH+PATHEXT resolution, `.cmd`/`.bat` routed through `cmd.exe /c` via `ArgumentList` (no shell string)

## Phase 9 — State & inspection tools ✅ core complete

- [x] **State browser** — `state list` + `show -json` (current state) parsed in memory into a redacted `StateSnapshot` (`StateJsonParser`: recurses child modules, redacts via the state `sensitive_values` map); two-pane browser (filterable address list + per-resource redacted attributes) ([22](22-terraform-files-model.md), [25](25-execution-lifecycle.md))
- [x] **Outputs** — `output -json` parsed (`OutputJsonParser`), sensitive outputs reduced to a placeholder; copy non-sensitive values ([06](06-plan-apply-safety.md))
- [x] **Dependency graph** — `terraform graph` → DOT parsed (`GraphDotParser`: `[root]`/`(expand)`/`(close)` conventions, escaped-quote provider ids, node classification) → **visual layered-DAG renderer** (offline `wwwroot/js/fenrix-graph.js`, pan/zoom, click-to-focus neighbours) — no external graph library
- [x] **Refresh-only drift** surfaced in the inspection view (delegates to the Phase 4 plan service; takes the lock, records a saved plan)
- [x] **Advanced state ops** — `state mv/rm/pull/push` + `force-unlock`; state-changing ops gated behind a typed confirmation (environment name) + the per-environment lock, **blocked when the environment is unbound** (Phase 8 authentication-required rule), redacted history; `state pull`/`show`/`output` output never logged (`captureLog:false`); `state pull` writes to a user-chosen backup file
- [x] **Workspace management** — `workspace list/select/new/delete`; select/new persist the environment's active workspace (`ProjectEnvironment.TerraformWorkspace`)
- [x] **Import assistant** — guided **CLI import** (`terraform import ADDRESS ID`: confirm + lock + blocked-when-unbound + history) and **config generation** (Terraform 1.5+ `import{}` block + `plan -generate-config-out`, generated HCL captured in file history and shown for review)
- [x] New **Inspect** ribbon tab + page (`/projects/{id}/inspect`) with State browser / Outputs / Graph / Workspaces / Drift / State ops / Import; reuses the command-preview, output-console, connection-bar, and plan-review components
- [x] Verified (MAUI not compiled here): `tests/terraform-fixtures/` real-format state/output/graph/workspace samples + Python reference port — 24/24 assertions (redaction, module recursion, DOT parsing, workspace current)
- [ ] _Follow-ups:_ read-only inspection currently records a redacted history row per run (like Phase 4 `show -json`) rather than running silently (Phase 5 Git posture) — could be made silent if it proves noisy; `state show` details are derived from `show -json` (structured + redactable) rather than the text `state show` (the literal command is still previewable); graph renderer uses a simple longest-path layering (no crossing-minimisation) — fine for typical project sizes; serial/lineage only populated when present (raw state / `state pull`), not from `show -json`

## Phase 8.5 — Project secrets & key-pair management ✅ core complete

- [x] Per-project **SSH/EC2 key-pair** management: import existing keys (PEM / OpenSSH / PuTTY `.ppk`) into a secure app folder ([28](28-key-pair-management.md))
- [x] **Generate** key pairs via Terraform on the backend (`tls_private_key`, optional `aws_key_pair`), auto-capture the sensitive output into the secure store — no AWS-console round-trip (local default + optional cloud register)
- [x] Encrypted-at-rest (DPAPI) private keys under `Data\keys\<projectId>\`; DB holds only a `KeyPair` metadata row + a `SecretReference` ([11](11-secrets.md))
- [x] Keys section inside the project (view fingerprint/public key/source; copy public key or secure path; rename/rotate/delete; gated+audited private-key export behind a Settings toggle + typed key-name confirm)
- [x] Reference-picker so `connection`/`provisioner`/`aws_key_pair` blocks point at a managed key (copy + HCL snippet + insert into a chosen `.tf` file)
- [x] **Needs one migration:** `AddKeyPairs` (new `KeyPair` entity → table `KeyPairs`). No other schema change.
- [ ] _Stretch:_ "Connect" (SSH to instance/bastion using a managed key); tfvars secret references; policy/cost/drift add-ons (see [28](28-key-pair-management.md))
- [ ] _Deferred:_ full PPK conversion for encrypted / non-RSA keys, and Ed25519-from-bare-PEM public derivation (dependency-free build; those keys still import + store, public shown when derivable)

## Phase 9.5 — CI/CD Pipelines & Deployments

- [x] `ProjectVersion` model (per-project, Git-anchored, semver labels) — cut-from-HEAD (optional annotated tag + push) + infer-from-tags; `SemVerLabel` tolerant parse/precedence ([20](20-pipelines-deployments.md))
- [x] `Deployment` records (version + state serial/lineage + plan summary) — written by the single `IDeploymentRecorder` after every successful apply (both the Plan & apply page and the governed flow); serial/lineage read from the local state file (only the two non-sensitive top-level fields)
- [x] Independent version-per-environment (v1/Live, v1.5/UAT, v2/Dev) — current version = latest `Succeeded` deployment per env
- [x] Version × environment matrix view (`VersionMatrixBuilder`; current/previous/available)
- [x] Deploy one version to many/all environments (governed fan-out) — auto-applies gate-clean targets, flags approval/production targets as needing confirmation
- [x] Read-only deployments board (from plan/apply + Git history) + external-pipeline status
- [x] Pipeline definitions + ordered stages (`DeploymentPipeline`/`PipelineStage`; editor with reorder)
- [x] Stage gates (approval [local self-ack], required branch, clean tree, production typed-confirm, promote-in-order) via `DeploymentGateEvaluator`
- [x] One-click governed deploy (plan → gates → apply the exact saved plan) — reuses the Phase 4 plan/apply spine; checkout-to-version guard so what deploys is what was cut
- [x] Promote (upstream version → downstream) & rollback (previous distinct succeeded version)
- [x] External-pipeline status on the board (Phase 7 provider adapters, capability-gated, best-effort)
- [ ] _Needs one migration: `AddDeploymentPipelines` (new `DeploymentPipelines` + `PipelineStages` tables). `ProjectVersions`/`Deployments` tables already exist from `AddSavedPlans`. Verify port `tests/deployment-fixtures/verify_deployments.py` (sandbox VM was down — run it)._

## Phase 10 — Visual resource builder

- [x] Provider-schema cache ([07](07-visual-builder.md)) — `providers schema -json` (new `ProvidersSchema` command kind, read-only, `captureLog:false`), `ProviderSchemaJsonParser` (types incl. list/set/map/object/tuple + nested blocks + nested_type; required/optional/computed/sensitive), offline cache under `Cache/terraform-schemas/<project>_<env>.json` + `.meta.json` (captured-at, provider count, provider-lock hash for staleness). `IProviderSchemaService`/`ProviderSchemaService`.
- [x] Provider/resource browser · schema forms · HCL preview · generation — provider→resource/data browser with search, recursive schema-driven `SchemaForm` (required first, optional collapsible, nested-block add/remove, expression escape-hatch), live HCL preview, write to a chosen/new `.tf` via the atomic-write + file-history path.
- [x] Round-trip editing of existing simple blocks — `HclLexer`/`HclReader` locate top-level blocks and classify each argument as a plain literal (editable) or complex expression (preserved as raw); edits applied as in-place value-span splices so unsupported HCL is preserved byte-for-byte.
- [x] Form authoring for all config-side files (providers, versions/terraform settings, variables, outputs, locals, tfvars, backends, data, modules) ([22](22-terraform-files-model.md)) — pure `ConfigHclBuilder` generators + `HclEmitter` (canonical 2-space HCL) behind per-file panels.
- [x] _No new migration (files are the source of truth; the schema cache is on disk). Templates deferred (they feed the Phase 11 enterprise metadata DB). Verify port `tests/builder-fixtures/verify_builder.py` (sandbox VM was down — run it)._

## Phase 10.5 — Terraform-aware code editor

Turn the plain `<textarea>` file editor on the project Files page into a professional, Terraform-focused code editor. Everything runs through the existing process runner + command preview (no shell strings); `fmt`/`validate` use the resolved Terraform binary. The editor foundation + `fmt`/`validate` ribbon are independent of provider/cloud work and can be pulled earlier as a quality pass; the schema-aware completion reuses the Phase 10 provider-schema cache (hence the placement).

**Editor foundation**

- [ ] Replace the textarea with a bundled, **offline** code-editor component — recommend **CodeMirror 6** (lightweight, HCL/Terraform language support; Monaco is the heavier fallback). Blazor↔JS interop wrapper; assets bundled in `wwwroot` (no CDN).
- [ ] **Line numbers** + current-line highlight + gutter; HCL **syntax highlighting** (blocks, strings, comments, `${…}` interpolations); bracket matching + auto-close; 2-space indent; whitespace/EOL normalization; word-wrap toggle; adjustable font size.
- [ ] Dark/Light themes matched to the app tokens; reduced-motion aware.

**Editor ribbon (Terraform-specific)**

- [ ] **Format ("Beautify")** — run `terraform fmt -` (stdin→stdout) on the buffer via the command catalog and replace the buffer with canonical formatting; optional **format-on-save** (Settings).
- [ ] **Validate** — run validate for the environment and surface diagnostics **inline** (gutter markers + squiggles), reusing the Phase 3 validate pipeline/parser.
- [ ] Comment/uncomment toggle (`#`); find & replace in file; go-to-line.
- [ ] **Snippet palette** — insert scaffolded HCL blocks (resource, variable, output, provider, module, backend, data, locals).
- [ ] **Outline / go-to-symbol** — jump to resource/variable/output/module blocks in the current file.
- [ ] **Reference helpers** — quick-insert `var.` / `local.` / `module.` / `data.` references (schema-aware once the Phase 10 cache exists).

**Safety & consistency**

- [ ] Editor writes go through the same atomic-write + file-history + command-preview path as today (unsaved-changes guard, dirty indicator preserved).
- [ ] No new secrets/shell strings; `fmt`/`validate` runs recorded as redacted history like other Terraform commands.

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
| 2026-07-28 | Managed private keys are DPAPI-encrypted under `Data\keys\<projectId>\` (never in the project/git); DB holds only a `KeyPair` row + `SecretReference`. Import reads the public key without decrypting the private half; generation captures it from Terraform outputs. Dependency-free SSH/PPK handling (no BouncyCastle) — Ivo's call | [28](28-key-pair-management.md), [11](11-secrets.md) |
| 2026-07-28 | Deployments recorded by a single `IDeploymentRecorder` inside the apply service, so every successful apply (Plan & apply page *or* governed Pipelines flow) lands on the board; resolves/creates the `ProjectVersion` from the plan's commit. Governed deploy = plan → gates → apply the exact saved plan (no bypass of ADR-0003); a deploy first checks the version's commit out so what deploys is what was cut. Approval gate is a local self-ack (multi-user role approvals stay Phase 11). State serial/lineage read from the local `terraform.tfstate` (only the two non-sensitive top-level fields). One migration: `AddDeploymentPipelines` (pipeline-config tables only) | [20](20-pipelines-deployments.md) |
