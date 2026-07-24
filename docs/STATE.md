# STATE — resume here

> One-screen handoff for starting a fresh session with minimal context. Read this first,
> then only the files it points to. Full detail lives in [PROGRESS.md](PROGRESS.md) and the numbered docs.

_Updated: 2026-07-24_

## Where we are

- **Phase 0 (design & docs):** ✅ complete — 27 topic docs + 5 ADRs in `docs/`.
- **Phase 1 (foundation):** ✅ built — solution structure, EF Core/SQLite, settings, themed Blazor shell.
- **Phase 2 (project management):** ✅ complete — project create/import, manifest, environments, recent/linked projects, file tree, `FileSystemWatcher` + reconciliation + change journal, DB-backed file history/recovery. EF switched to migrations. Solution builds.
- **Phase 3 (Terraform execution foundation):** ✅ core complete — Terraform discovery + version/constraint enforcement, safe process runner (ArgumentList, cancellation, tree-kill, structured events), live streaming output, redacted DB-backed command history, typed Init/Format/Validate/Version screens, and the shared live command-preview component. Deferred to a follow-up: dynamic `-help` builder + ConPTY terminal.
- **Phase 4 (Plans & deployment safety):** ✅ core complete — saved plan (`plan -out`) + `show -json` parsing (redacted, in-memory), three-pane review (summary cards, filters, before/after) with sensitive-value redaction, config/lock/plan hashing + invalidation, apply of the exact saved plan (`apply -json` with structured per-resource progress), production typed-confirmation, destroy + refresh-only (drift) workflows, and on-disk per-environment operation locks. Deferred to Phase 5: Git provenance in plan metadata.
- **Next:** Phase 5 — Git core (repo detection/init/clone, status parsing, stage/commit/push, command preview, branches/history/diff). This also unblocks the deferred plan-safety Git provenance (commit/branch/uncommitted warnings). See [PROGRESS.md](PROGRESS.md#phase-5--git-core) and [08](08-git-engine.md).

## Before running (one-time migration)

Generate the EF migration (design-time factory is in place), then smoke-test:

```
dotnet ef migrations add InitialCreate -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure
```

If `InitialCreate` already exists from before Phase 3, add a follow-up migration instead (the new `CommandRun` config adds indexes + column lengths), e.g. `AddCommandRunHistory`. **You no longer need to delete a pre-migration `fenrix.db`:** `AppInitializer` now *adopts* a legacy `EnsureCreated` database (schema present, no `__EFMigrationsHistory`) by creating any missing model tables and stamping the migrations as applied — existing data is preserved and all future changes migrate incrementally. Terraform must be on `PATH` or set in Settings (`terraform.executable`) for the Terraform screens to resolve a binary.

**Phase 4 migration:** generate `AddSavedPlans` for the new `SavedPlan` entity (table `SavedPlans`) before running:

```
dotnet ef migrations add AddSavedPlans -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure
```

No other schema changed. Saved plans are written to `plans/<env>/*.tfplan` and env locks to `.fenrix/locks/` inside each project; both, plus `*.tfstate` and `.terraform.lock.hcl`, are now git-tracked (scaffolder `.gitignore` updated). Keep project repos private — plan/state files hold plaintext secrets.

## What exists (Phase 1)

- Class libraries under `src/`: `Domain`, `Contracts`, `Application`, `Infrastructure` (app project stays at repo root; `src/**` excluded from its globs).
- Persistence: `AppDbContext` (SQLite) + `WorkspacePaths` (+ LOCALAPPDATA fallback) + `AppInitializer` (EnsureCreated on launch).
- `SettingsService` with scope resolution (env → project → global → default).
- Blazor shell: rail + top bar + status bar; **Dark/Light** theme (dark default) via tokens in `wwwroot/css/fenrix.css`; pages Dashboard/Projects/Connections/SourceControl/Activity/Templates/Help/Settings.

## What exists (Phase 2)

- Domain: `Files/{FileIdentity,FileVersion,FileBlob,FileChangeKind,ChangeOrigin}`; `InfrastructureProject.IsLinked`.
- Contracts: `Projects/*` (manifest, create/import requests, scan result, mappings, summary) + `Files/*` (tree node, change, history, recoverable).
- Application: abstractions under `Abstractions/{Projects,Files}`; `Files/FileTrackingPolicy` (ignore/versioned rules) + `FileHashing`.
- Infrastructure: `Projects/{ProjectService,ProjectScaffolder,ProjectManifestStore,ProjectImportScanner}`; `Files/{FileTreeService,FileHistoryStore,ProjectFileSynchronizer,ChangeJournal,RecycleBin}`; EF configs + DbSets for the file-history tables; `AppDbContextFactory` (design-time) + `AppInitializer` now Migrate-with-EnsureCreated-fallback.
- UI: rewritten `Projects` page (list/recent/new/import), `ProjectFiles` page (tree + editor + history + recoverable), `NewProjectDialog`, `ImportProjectDialog`, `FileTreeView`, shared `Modal`; `IFolderPicker` (Windows WinRT) in `Services`; extended `Icon` set + CSS.
- Route: `/projects/{id}` opens a project; synchronizer starts on open, stops on dispose.

## What exists (Phase 3)

- Domain: `Terraform/{TerraformVersion, TerraformVersionConstraint, TerraformInstallation, TerraformExecutableSource, TerraformRunStatus}`. Constraint parser covers the full `required_version` grammar (`=,!=,>,>=,<,<=,~>`, comma-AND) with correct prerelease precedence; validated against 16 cases.
- Contracts: `Terraform/*` — `TerraformCommandRequest`, `TerraformCommandKind`, `Init/Format/ValidateOptions`, `ProcessOutputEvent`, `ProcessResult`, `CommandPreview`(+`CommandContextChip`), `TerraformValidationResult`(+`ValidationDiagnostic`), `CommandRunSummary`, `TerraformRunSpec/Plan/Result`.
- Application: abstractions `Abstractions/Terraform/{IProcessRunner, ITerraformDiscovery, ICommandHistoryStore, ITerraformExecutor}`; pure logic `Terraform/{TerraformCommandCatalog, CommandPreviewBuilder, ArgumentRedactor}` (the catalog is the single source of the `ArgumentList`, so preview == execution).
- Infrastructure: `Processes/ProcessRunner`; `Terraform/{TerraformDiscovery, TerraformExecutor, EfCommandHistoryStore}`; `CommandRunConfiguration` in `Configurations.cs`; DI registered (runner singleton; discovery/history/executor scoped).
- UI: `Components/Terraform/{CommandPreviewPanel, OutputConsole}`; `Components/Pages/TerraformRun.razor` at `/projects/{id}/terraform` (env selector, Init/Format/Validate/Version, live preview + streaming + redacted history); "Terraform" button added to the project files page; `scrollToBottom` + `copy`/`terminal`/`stop` icons added.

## What exists (Phase 4)

- Domain: `Terraform/{SavedPlan, PlanMode}`. `SavedPlan` holds plan-file paths, integrity hashes (config/lock/plan), redacted counts, env snapshot (production, cloud connection), apply lifecycle, invalidation, and nullable Git provenance (populated in Phase 5).
- Contracts: `Terraform/*` — `PlanOptions`+`ApplyConfirmation`; extended `TerraformCommandKind` (Plan/Apply/Show) + `TerraformRunSpec` (Plan/VarFile/OutPlanFile/PlanFilePath); `PlanReview`(+`PlanResourceChange`,`ChangeAction`,`ResourceMode`,`AttributeChange`,`PlanOutputChange`,`PlanChangeSummary`); `SavedPlanSummary`; `PlanContext`+`PlanCreationResult`; `ApplyPreflight`(+`PreflightCheck`,`PreflightSeverity`,`ApplyProgressEvent`,`ApplyResult`).
- Application: abstractions `Abstractions/Terraform/{IEnvironmentLockService(+IEnvironmentLock), ISavedPlanStore, ITerraformPlanService, ITerraformApplyService}`; pure logic `Terraform/{PlanJsonParser (show-json → redacted review), ApplyJsonParser (apply -json events + change_summary), PlanIntegrity (config/lock hashing + invalidation)}`; `TerraformCommandCatalog` extended with plan/apply/show builders (still the single `ArgumentList` source).
- Infrastructure: `Terraform/{FileEnvironmentLockService (on-disk `.fenrix/locks`), EfSavedPlanStore, TerraformProcessCoordinator (shared run+history+log; `captureLog:false` for `-json` so sensitive JSON is never logged), TerraformIntegrity (project-local paths + config/lock hashing), TerraformPlanService, TerraformApplyService}`; `SavedPlanConfiguration` in `Configurations.cs` + `SavedPlans` DbSet; DI registered (lock service singleton; coordinator/store/plan/apply scoped). `FileTrackingPolicy` versions `.tfplan`/`.tfstate`; scaffolder `.gitignore` tracks plans/state/lock, ignores `.fenrix/locks/`.
- UI: `Components/Terraform/{PlanReviewPanel, ApplyProgressView}`; `Components/Pages/PlanApply.razor` at `/projects/{id}/plan` (env + plan-type selector, live preview + streaming create-plan, three-pane review with filters, apply preflight/warnings/production typed-confirm, live per-resource progress + raw console, saved-plans list); "Plan & apply" buttons on the project files + Terraform pages; `shield`/`zap`/`layers`/`swap`/`minus` icons + Phase 4 CSS.
- Verified: `PlanJsonParser`/`ApplyJsonParser`/`PlanIntegrity` logic checked against real `show -json`/`apply -json` fixtures via a reference cross-check (25 assertions passing) — MAUI itself isn't compiled in the authoring environment.

## Key decisions (don't re-litigate)

- Drive official CLIs; files are source of truth; saved-plan-only apply. (ADRs 0001–0003)
- Cloud connection is bound **per environment**, never on the project. (ADR-0005)
- Versions are per-project, deployed independently per environment. ([20](20-pipelines-deployments.md))
- Theme is **Dark + Light only** (no System); Dark default.
- DB schema now via **EF migrations**. `AppInitializer` is upgrade-safe and never requires a reset: a migration-controlled DB gets pending migrations applied; a **legacy `EnsureCreated` DB is adopted** (missing model tables created + migrations stamped as applied); a fresh/empty DB is created from migrations; and with no migrations authored it falls back to `EnsureCreated`. Files are source of truth; the file-history store is a recovery cache (dedup by SHA-256, GZip). In-app delete of tracked files is **off by default** (Settings → Security); external deletes are detected and recoverable.
- Terraform engine lives **in Infrastructure** (not a separate `Fenrix.IaCStudio.Terraform` project yet) to match the Phase 2 precedent and avoid a blind `.csproj`/`.slnx` change. Commands run via **`ArgumentList` only** — never a shell string. The **preview and the executed process share one argument list** (`TerraformCommandCatalog`), so they can't diverge. Redacted history is persisted (`CommandRun`); raw output goes to `Logs/terraform/<runId>.log`, never the DB.
- On **SQLite**, all `DateTimeOffset` columns use `DateTimeOffsetToBinaryConverter` (SQLite can't `ORDER BY`/compare `DateTimeOffset` in SQL); applied via `ConfigureConventions`, guarded to SQLite only. If you change this, regenerate the migration.
- **Apply only the exact saved plan** (ADR-0003): `plan -out` → `show -json` (parsed in memory, redacted, never persisted raw) → `apply -json <plan>`. Every plan is its own file so any reviewed plan stays applyable. Destroy = `plan -destroy` then apply; drift = `plan -refresh-only`. A plan is **invalidated** when the config or provider-lock hash changes after creation.
- Everything for a project stays **inside the project folder**: plans in `plans/<env>/`, env locks in `.fenrix/locks/`. Plans, state (`*.tfstate`), and the provider lock are **version-controlled** (git + Fenrix file history) — Ivo's call; repos must be private because plan/state files carry plaintext secrets. Locks are the exception (ephemeral, gitignored).
- Per-environment **operation lock** is an on-disk lock file (`FileEnvironmentLockService`): exclusive-create is the lock, PID recorded for staleness/force-release; only one state-changing op per env; read-only `show` doesn't lock.
- `-json` command output (`show`/`apply`) is **never written to a log file** (it can contain unredacted sensitive values); only the human-readable `plan` run is logged (Terraform already masks sensitive there). Redacted history rows are still recorded for all runs.

## Git / workflow

- Repo: `github.com/IvayloDGeorgiev/Fenrix-Terraform-UI`, work on branch `develop`.
- All git commands run by the user in Visual Studio (the assistant only reads git state, never writes — sandbox git left a lock last time).
- Commit at each phase boundary with a descriptive message.

## How to resume in a new chat

Say: _"Read docs/STATE.md and continue with Phase N."_ The assistant reads this file (+ PROGRESS.md and the relevant numbered docs/code) and picks up — no need to replay history.
