# STATE — resume here

> One-screen handoff for starting a fresh session with minimal context. Read this first,
> then only the files it points to. Full detail lives in [PROGRESS.md](PROGRESS.md) and the numbered docs.

_Updated: 2026-07-24_

## Where we are

- **Phase 0 (design & docs):** ✅ complete — 27 topic docs + 5 ADRs in `docs/`.
- **Phase 1 (foundation):** ✅ built — solution structure, EF Core/SQLite, settings, themed Blazor shell.
- **Phase 2 (project management):** ✅ complete — project create/import, manifest, environments, recent/linked projects, file tree, `FileSystemWatcher` + reconciliation + change journal, DB-backed file history/recovery. EF switched to migrations. Solution builds.
- **Phase 3 (Terraform execution foundation):** ✅ core complete — Terraform discovery + version/constraint enforcement, safe process runner (ArgumentList, cancellation, tree-kill, structured events), live streaming output, redacted DB-backed command history, typed Init/Format/Validate/Version screens, and the shared live command-preview component. Deferred to a follow-up: dynamic `-help` builder + ConPTY terminal.
- **Next:** Phase 4 — plans & deployment safety (saved plan `-out`, `show -json` parsing, 3-pane change view, apply-exact-saved-plan, production confirmation, per-environment locks). See [PROGRESS.md](PROGRESS.md#phase-4--plans--deployment-safety) and [06](06-plan-apply-safety.md), [25](25-execution-lifecycle.md).

## Before running (one-time migration)

Generate the EF migration (design-time factory is in place), then smoke-test:

```
dotnet ef migrations add InitialCreate -p src/Fenrix.IaCStudio.Infrastructure -s src/Fenrix.IaCStudio.Infrastructure
```

If `InitialCreate` already exists from before Phase 3, add a follow-up migration instead (the new `CommandRun` config adds indexes + column lengths), e.g. `AddCommandRunHistory`. Delete any dev `fenrix.db` from a pre-migration `EnsureCreated` run first (that schema has no `__EFMigrationsHistory`). Terraform must be on `PATH` or set in Settings (`terraform.executable`) for the Terraform screens to resolve a binary.

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

## Key decisions (don't re-litigate)

- Drive official CLIs; files are source of truth; saved-plan-only apply. (ADRs 0001–0003)
- Cloud connection is bound **per environment**, never on the project. (ADR-0005)
- Versions are per-project, deployed independently per environment. ([20](20-pipelines-deployments.md))
- Theme is **Dark + Light only** (no System); Dark default.
- DB schema now via **EF migrations** (auto-falls back to `EnsureCreated` until the first migration exists). Files are source of truth; the file-history store is a recovery cache (dedup by SHA-256, GZip). In-app delete of tracked files is **off by default** (Settings → Security); external deletes are detected and recoverable.
- Terraform engine lives **in Infrastructure** (not a separate `Fenrix.IaCStudio.Terraform` project yet) to match the Phase 2 precedent and avoid a blind `.csproj`/`.slnx` change. Commands run via **`ArgumentList` only** — never a shell string. The **preview and the executed process share one argument list** (`TerraformCommandCatalog`), so they can't diverge. Redacted history is persisted (`CommandRun`); raw output goes to `Logs/terraform/<runId>.log`, never the DB.
- On **SQLite**, all `DateTimeOffset` columns use `DateTimeOffsetToBinaryConverter` (SQLite can't `ORDER BY`/compare `DateTimeOffset` in SQL); applied via `ConfigureConventions`, guarded to SQLite only. If you change this, regenerate the migration.

## Git / workflow

- Repo: `github.com/IvayloDGeorgiev/Fenrix-Terraform-UI`, work on branch `develop`.
- All git commands run by the user in Visual Studio (the assistant only reads git state, never writes — sandbox git left a lock last time).
- Commit at each phase boundary with a descriptive message.

## How to resume in a new chat

Say: _"Read docs/STATE.md and continue with Phase N."_ The assistant reads this file (+ PROGRESS.md and the relevant numbered docs/code) and picks up — no need to replay history.
