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

_Status: **core complete** (2026-07-29). Editor engine is a **hand-rolled, dependency-free** vanilla-JS HCL editor (`wwwroot/js/fenrix-editor.js`) — Ivo's call, since CodeMirror 6 can't be bundled offline here without an npm/rollup build step, and it matches the house style (the hand-rolled `fenrix-graph.js` renderer + HCL toolkit). Full ribbon + schema-aware reference helpers done in one batch. **No DB migration.** Verified by hand-trace only — the sandbox VM was down, so nothing was executed; run a build + the editor smoke test in Visual Studio._

**Editor foundation**

- [x] Replace the textarea with a bundled, **offline** code-editor component — **hand-rolled dependency-free** editor (`fenrix-editor.js`) instead of CodeMirror 6 (can't bundle CM6's ES modules offline without a build toolchain; hand-rolled matches the dependency-free house style). Blazor↔JS interop wrapper (`Components/Editor/CodeEditor.razor`); assets in `wwwroot` (no CDN).
- [x] **Line numbers** + current-line highlight + gutter; HCL **syntax highlighting** (blocks, strings, comments, `${…}`/`%{…}` interpolations, heredocs, numbers, functions); bracket matching + auto-close; 2-space indent (auto-indent on Enter, Tab/Shift-Tab block indent); word-wrap toggle; adjustable font size.
- [x] Dark/Light themes matched to the app tokens (`--fx-cm-*` variables); reduced-motion aware (no transitions).

**Editor ribbon (Terraform-specific)**

- [x] **Format ("Beautify")** — runs `terraform fmt -` (stdin→stdout) on the buffer via the command catalog (`FormatStdin` kind), replaces the buffer with canonical formatting; optional **format-on-save** (Settings → Code editor).
- [x] **Validate** — runs the Phase 3 validate pipeline for the environment and surfaces diagnostics **inline** (gutter markers + wavy squiggles for the current file) plus a full diagnostic list.
- [x] Comment/uncomment toggle (`#`, Ctrl+/); find & replace in file; go-to-line.
- [x] **Snippet palette** — inserts scaffolded HCL blocks (resource, variable, output, provider, module, backend, data, locals) via `EditorSnippetCatalog`.
- [x] **Outline / go-to-symbol** — jumps to resource/variable/output/module/data/locals blocks in the current file (`EditorOutlineBuilder`).
- [x] **Reference helpers** — quick-insert `var.` / `local.` / `module.` / `data.` / resource references (`ReferenceIndexBuilder`), schema-aware attribute chips reusing the Phase 10 provider-schema cache.

**Safety & consistency**

- [x] Editor writes go through the same atomic-write + file-history path as today (`IFileTreeService.WriteFileAsync`); unsaved-changes guard + dirty indicator preserved.
- [x] No new secrets/shell strings; `fmt` runs via the shared `ArgumentList` catalog with the buffer piped through **stdin** (never in args/history/log; `captureLog:false`) and recorded as redacted history; `validate` recorded like other Terraform commands.

## Phase 11 — Enterprise capability

Turn the single-user desktop into an organisation-governed tool: a shared **metadata** layer (SQLite by
default / **SQL Server** opt-in), an **identity** seam, **role-based restrictions**, **central audit**,
**shared policies + templates**, **role-gated approvals**, **organisation-controlled Terraform versions**, and
the **Fenrix Agent** seam (design only). Scope decisions (Ivo, 2026-07-29): **one big batch**; **full
dual-provider** SQL Server wiring; **Windows identity now** behind a pluggable `IUserContext`; the **Agent is
design-only** (seam + docs, no service). See [29-enterprise.md](29-enterprise.md), [30-fenrix-agent.md](30-fenrix-agent.md),
[ADR-0006](adr/0006-enterprise-metadata-and-identity.md), [ADR-0007](adr/0007-execution-host-seam.md).

_Status: **backends + docs complete** (2026-07-29); **UI pages + call-site enforcement wiring remain** as the
next clean boundary (see below). Needs **two migration sets** (SQLite + SQL Server). **Nothing was executed —
the sandbox VM was down this session**, so the reference port is hand-traced; build + run in Visual Studio._

**Foundation (done)**

- [x] `IUserContext` (Application) + `WindowsUserContext` (Infrastructure, SID + name + UPN, safe fallback);
  replaced inlined `Environment.UserName` in `DeploymentRecorder` + `ProjectVersionService`.
- [x] Dual-provider `AppDbContext`: added `Microsoft.EntityFrameworkCore.SqlServer`; provider chosen at DI time
  from the `enterprise.json` bootstrap (`EnterpriseBootstrap`/`IEnterpriseConfig`); `DateTimeOffset`-as-binary
  stays SQLite-only; design-time factory switches provider via `FENRIX_DESIGNTIME_PROVIDER`.
- [x] `IExecutionHost` seam + `LocalExecutionHost` (only impl this phase) so governed runs can route to a future
  agent without touching callers (ADR-0007). No service.

**Enterprise metadata + governance (backends done)**

- [x] **RBAC** — `OrgUser`/`OrgRole`/`Permission [Flags]`/`RoleAssignment` (Global/Project/Environment scope);
  pure `PermissionEvaluator`; `IAuthorizationService` (allow-all when mode off, else union of in-scope grants,
  audits denials); `IRoleService` admin CRUD + current-user upsert; `EnterpriseSeeder` (built-in roles +
  bootstrap Administrator on first enterprise run).
- [x] **Central audit** — `AuditEvent` + `IAuditService` (best-effort redacted sink to the metadata DB +
  filtered paged reader); the doc-15 catalogue + Phase 11 additions (role/policy/template/approval/export/denied).
- [x] **Shared policies** — `OrgPolicy` (approve-prod / approve-envs / block-prod-destroy / require-private-repo
  / required-prod-branch / allowed-TF-version); pure `PolicyEvaluator`; `IPolicyService` (single active row).
- [x] **Shared templates** — `ConfigTemplate` + `TemplateParameter`; pure `TemplateInstantiator` (typed
  `{{placeholder}}` substitution, String quoted via `HclEmitter`); `ITemplateService` writes through the Phase 10
  authoring atomic-write path; stored in the metadata DB. (The Phase 10 deferred templates.)
- [x] **Approvals** — `ApprovalRequest`; pure `ApprovalResolver` (separation-of-duties + `ApproveDeployment`
  gate + expiry); `IApprovalService` (create / role-scoped inbox / decide / `IsPlanApprovedAsync`) — replaces the
  Phase 9.5 local self-ack.
- [x] **Org-controlled Terraform versions** — `OrgPolicy.AllowedTerraformVersionConstraint` +
  `IPolicyService.CheckTerraformVersionAsync` reusing the Phase 3 `TerraformVersionConstraint` grammar.
- [x] DI registered (identity/execution singletons; audit/authz/role/policy/template/approval + seeder scoped);
  `AppInitializer` runs `EnterpriseSeeder` after schema bring-up (no-op when mode off).
- [x] Reference port `tests/enterprise-fixtures/verify_enterprise.py` (PermissionEvaluator / PolicyEvaluator /
  TemplateInstantiator / ApprovalResolver) — hand-traced; **run it** (VM was down).

**UI + wiring (done 2026-07-30; no new schema) — build + smoke-test in Visual Studio (VM was down):**

- [x] UI pages: **Enterprise admin** (`/enterprise/admin` — roles + permission grid, users, role assignments at
  Global/Project/Environment scope, org-policy editor), **Audit viewer** (`/enterprise/audit` — filtered + paged
  over `IAuditService.QueryAsync`), **Template gallery** in the Build page (new **Templates** tab →
  `TemplateGalleryPanel`: pick → fill parameters → preview → apply via the Phase 10 authoring path; authoring
  gated by `ManageTemplates`), **Approvals inbox** (`/enterprise/approvals` — decide with note), and a read-only
  **Settings → Enterprise** status card from `IEnterpriseConfig.Status`. Nav shows an **Enterprise** group only
  when governance is enabled. New CSS (`.fx-table`, `.fx-checkline`, `.fx-permgrid`, `.fx-filterbar`, `.fx-pager`,
  `.fx-approval*`).
- [x] Enforcement wiring via `IAuthorizationService.AuthorizeAsync` at the guarded call sites: **key export**
  (`KeyPairService.ExportPrivateKeyAsync` → `ExportPrivateKey`), **state ops / force-unlock**
  (`TerraformStateService.ExecuteAsync`/`PullToFileAsync` → `ManageState`/`ForceUnlock`), **plan/destroy**
  (`TerraformPlanService.PreparePlanAsync` → `RunPlan`, +`RunDestroy` for destroy), **apply** (folded into the
  apply preflight, below), and **admin CRUD** (`RoleService`/`PolicyService`/`TemplateService` mutations →
  `ManageRoles`/`ManagePolicy`/`ManageTemplates`). Audit reads gate at the page (`ViewAudit`) to avoid an
  `AuditService`↔`AuthorizationService` DI cycle.
- [x] Folded `IPolicyService` + `IApprovalService` into the governed deploy + apply preflight: **apply preflight**
  (`TerraformApplyService.PreflightAsync`) adds RBAC (`RunApply`/`RunApplyProduction`/`RunDestroy`), the
  org-controlled Terraform-version block, the shared-policy hard blocks (**production-destroy**, required prod
  branch, **private-repo** — visibility resolved via the Phase 7 host adapter, enforced now), and the role-gated
  **approval** check — all no-op when mode off. `DeploymentService` replaces the self-ack: enterprise mode →
  `IApprovalService.IsPlanApprovedAsync`; mode off → the prior self-ack is preserved (a single user can't approve
  their own request). `DeployPreparation` gained `ApprovalGranted`/`ApprovalRequested`/`UsesRoleGatedApproval`;
  the deploy dialog shows request → awaiting → approved (enterprise) or the self-ack checkbox (off).
- [ ] **Not compiled here (sandbox VM down):** build on develop + smoke-test the pages and the governed-deploy
  approval path with `enterprise.json` toggled on/off. No new migration.

## Phase 12 — Release preparation

See [31-release-prep.md](31-release-prep.md) for the master checklist. Status (2026-07-31):

- [x] **UI readability + polish sweep** ([24](24-visual-design-language.md)) — systematic pass appended as a
      *Phase 12 polish* section in `fenrix.css` (overrides only): visible `:focus-visible` keyboard focus on all
      controls, an 11px text floor (three sub-11px micro-labels fixed), text-warning badges promoted to
      auto-sized pills app-wide (the `.fx-badge` 20×20-square bug), `--fx-faint` contrast raised to ~AA on both
      themes, narrow side-column `flex-wrap` + truncation `title`s. Reduced-motion already global. **(VS: verify
      visually in Dark + Light.)**
- [x] **Crash recovery · DB backup** — `IBackupService`/`SqliteBackupService` (online-backup snapshots to
      `Backups/`, bounded retention, SQL-Server skip), wired into `AppInitializer` (apply staged restore → crash
      detect → pre-migration snapshot → session marker) + clean-shutdown `EndSession` in `App.xaml.cs`. New
      services only; no edits to verified services; no new migration.
- [x] **MSIX packaging · update mechanism** — `Package.appxmanifest` file associations (`.tf/.tfvars/.hcl`,
      `.tfplan`, `.fenrixproject`) + `fenrix-iac://` protocol (MSIX-only, additive); `win-msix.pubxml` publish
      profile. **(VS: set real Identity/Publisher, build the package.)**
- [x] **Code signing** — documented in [31](31-release-prep.md) (EV/OV cert, `signtool`, Publisher↔cert match).
      **(VS: sign with a real cert.)**
- [x] **Security review · performance** — spot-checked: argv-only (zero `UseShellExecute = true`), secrets as
      references only, `-json` never logged, preview == execution. Perf checklist in [31](31-release-prep.md).
- [x] **User docs · example projects** — [user-guide.md](user-guide.md) + credential-free `examples/hello-fenrix`.
- [x] **Full Terraform coverage (closes AC 17)** — new **Commands builder** (`-help`-driven, every installed
      command, run through the safe spine; mutating ones redirect to their dedicated screens via
      `TerraformCommandClassifier`) + **embedded ConPTY Terminal** (interactive catch-all, hand-rolled VT
      renderer). New `TerraformCommandKind.Custom`; pure `TerraformHelpParser`; **no new migration**. Terminal
      **(VS: validate native ConPTY on a build)**.
- [x] **Help page** rewritten into a full documentation hub (deep dive per feature + Terraform & Git cheatsheets).
- [ ] **Phase 11 close-out (VS)** — build/smoke-test the 5 surfaces + governed-deploy approval (enterprise.json
      ON/OFF, mode-off byte-for-byte) and run `tests/enterprise-fixtures/verify_enterprise.py` (hand-traced green,
      37/37). Checklist in [31 §0](31-release-prep.md).
- [ ] **Signed MSIX + stable channel (VS)** — the remaining gate for AC 1; cut `v1.0.0` after.

## Acceptance criteria (see [ROADMAP.md](ROADMAP.md#acceptance-criteria))

- **19 of 20 met** (criteria 2–20). **AC 17** now closed by the Phase 12 Commands builder + embedded Terminal.
  **AC 1** (install on Windows) is the only remaining gate — build + sign the MSIX **(VS)**. Full table in
  [31 §8](31-release-prep.md).

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
| 2026-07-29 | Phase 11 enterprise: **dual-provider metadata** (SQLite default / SQL Server opt-in) chosen at DI time from an `enterprise.json` **bootstrap** (connection string via a named env var, never on disk); **Windows identity** behind a pluggable `IUserContext` (Entra/OIDC later); **additive RBAC that only tightens** (allow-all when mode off) with a pure `PermissionEvaluator`; **central audit** to the metadata DB; **shared policy/templates**; **role-gated approvals** replacing the Phase 9.5 self-ack; **Fenrix Agent = design-only** via an `IExecutionHost` seam (local impl only). One big batch (Ivo). | [29](29-enterprise.md), [30](30-fenrix-agent.md), [ADR-0006](adr/0006-enterprise-metadata-and-identity.md), [ADR-0007](adr/0007-execution-host-seam.md) |
| 2026-07-29 | Phase 10.5 code editor is a **hand-rolled, dependency-free** vanilla-JS HCL editor (`fenrix-editor.js`), not CodeMirror 6 — CM6's ES modules can't be bundled offline here without an npm/rollup toolchain, and hand-rolling matches the dependency-free house style (`fenrix-graph.js`, the HCL toolkit). "Beautify" = `terraform fmt -` over the buffer via **stdin** (new `FormatStdin` kind + a `StandardInput` field threaded through the request/runner; `captureLog:false`, never in args/history/log). Validate reuses the Phase 3 pipeline for inline gutter markers/squiggles. Outline/snippets/reference-helpers are pure Application logic; references are schema-aware via the Phase 10 cache. Saves keep the atomic-write + file-history path. **No new migration.** | [05](05-terraform-engine.md), [13](13-ui-design.md) |
| 2026-07-31 | Phase 12 UI polish applied as an **append-only** *Phase 12 polish* section in `fenrix.css` (overrides only, reviewable/revertible): global `:focus-visible`, 11px text floor, `.fx-badge.warn` promoted to an auto-sized pill app-wide (the 20×20-square text-wrap bug), `--fx-faint` AA contrast on both themes, narrow-column `flex-wrap` — Ivo's call (big batch; append a section). | [24](24-visual-design-language.md), [31](31-release-prep.md) |
| 2026-07-31 | Phase 12 **DB backup + crash recovery** as new services only (no edits to verified services, no new migration): `IBackupService`/`SqliteBackupService` uses SQLite's online-backup API (WAL-safe), keeps a bounded history under `Backups/`, **skips** when the metadata store is external SQL Server, and stages restores to apply at next launch before the context opens. `AppInitializer` takes a pre-migration snapshot + detects unclean shutdown via a session marker; `App.xaml.cs` clears it on clean shutdown. | [12](12-database-design.md), [18](18-packaging-deployment.md), [31](31-release-prep.md) |
| 2026-07-31 | Phase 12 **packaging** is additive: `Package.appxmanifest` gains file associations + `fenrix-iac://` (MSIX-only, dev loop untouched) and a `win-msix.pubxml` profile; real Identity/Publisher + signing + the MSIX build are done in VS. | [18](18-packaging-deployment.md), [31](31-release-prep.md) |
| 2026-07-31 | Phase 12 **project templates** — pick a complete, cost-aware starter when creating a project; it prefills every environment's working dir (networking + security + compute/storage). `IProjectTemplateService`/`ProjectTemplateService`: built-in catalog in code (`BuiltInTemplates`), user templates as JSON under `<dataRoot>\Templates` (no DB). New Project "Start from" Blank/Template picker + a `/templates` management page (browse, view files, create-from-project, delete). Cost philosophy: no NAT gateway where avoidable, Graviton/ARM, scale-to-zero/free-tier for demos, cheapest managed options (db.t4g.micro over Aurora, S3+CloudFront+OAC, single VM+Docker over managed k8s). **20 built-ins** — AWS ×8, Azure ×6, GCP ×4, Kubernetes + Local Docker — spanning static sites, serverless (free-tier), containers (scale-to-zero), VMs, networking, managed Postgres, and a remote-state backend. Grounded in current best-practice web research, not vendor defaults. | [32](32-project-templates.md) |
| 2026-07-31 | Phase 12 **one-click Terraform install** — when no binary is found, an **Install Terraform** button downloads the official HashiCorp Windows build (checkpoint API → releases.hashicorp.com), verifies the published SHA-256, unzips `terraform.exe` into the **shared** `<dataRoot>\Tools\terraform\` (app-level, not per-project), and sets `terraform.executable` at **Global** scope so every project resolves it. `ITerraformInstaller`/`TerraformInstaller` via the registered `IHttpClientFactory`; no admin/PATH changes. | [05](05-terraform-engine.md), [14](14-settings.md) |
| 2026-07-31 | Phase 12 **full Terraform coverage (closes AC 17)** — Ivo's call to build both: a **Commands builder** (`terraform -help`-driven; new `TerraformCommandKind.Custom` carries an arg list through the one catalog→runner spine, preview == execution, redacted history; `TerraformCommandClassifier` redirects mutating commands — apply/destroy/import/state mv|rm|push/force-unlock/workspace new|delete|select/taint/untaint/login/logout — to their dedicated safe screens so ADR-0003 + locking hold) and an **embedded ConPTY Terminal** (`ITerminalService`/`ConPtyTerminalService` Win32 pseudo-console + hand-rolled dependency-free VT renderer `fenrix-terminal.js`; Windows-only; needs a VS build to validate the native plumbing). Pure `TerraformHelpParser`. Help page rewritten into a documentation hub with Terraform + Git cheatsheets. **No new migration.** | [05](05-terraform-engine.md), [23](23-command-transparency.md), [31](31-release-prep.md) |
