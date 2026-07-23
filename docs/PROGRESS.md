# Progress Tracker

Living record of where the project stands. Update this in the same PR as the work it describes. Tick items as they meet the [Definition of Done](WORKFLOW.md#definition-of-done).

**Legend:** `[ ]` not started · `[~]` in progress · `[x]` done

_Last updated: 2026-07-23 — status: **Phase 1 foundation in progress** (solution, persistence, settings, theme, nav shell built; open in Visual Studio to build/run on Windows)._

## Milestone summary

| Phase | Title | Status |
|-------|-------|--------|
| 0 | Design & documentation | **Done** |
| 1 | Foundation | **In progress** |
| 2 | Project management | Not started |
| 3 | Terraform execution foundation | Not started |
| 4 | Plans & deployment safety | Not started |
| 5 | Git core | Not started |
| 6 | Advanced Git | Not started |
| 7 | Provider integrations | Not started |
| 8 | Cloud connections | Not started |
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
light/dark/system toggle that persists, Dashboard/Projects/Connections/Activity/Templates/Help/Settings
pages, SQLite DB + workspace tree created on first launch._

## Phase 2 — Project management

- [ ] Create project (recommended structure) ([03](03-domain-model.md))
- [ ] Import existing project wizard (no restructuring)
- [ ] Project manifest read/write (`.fenrix/project.json`)
- [ ] Default Dev/UAT/Live + custom environments
- [ ] Linked external projects
- [ ] Recent projects
- [ ] File tree + create/rename/move/delete (Recycle Bin)
- [ ] `FileSystemWatcher` + reconciliation + change journal ([04](04-filesystem-sync.md))
- [ ] File version history capture (create/update snapshots, dedup, compression) ([21](21-file-history-recovery.md))
- [ ] Recover accidentally deleted files (in-app delete disabled by default; external deletes recoverable)
- [ ] `IFileHistoryStore` works on both SQLite and SQL Server

## Phase 3 — Terraform execution foundation

- [ ] Terraform discovery + version detection + constraint enforcement ([05](05-terraform-engine.md))
- [ ] Process runner (`ArgumentList`, cancellation, tree-kill, structured events)
- [ ] stdout/stderr streaming to UI
- [ ] Command history (redacted)
- [ ] Typed screens: init, format, validate, version
- [ ] Command-preview component — show exact command per action, live-updating, redacted, copyable ([23](23-command-transparency.md))
- [ ] Dynamic raw command builder + embedded ConPTY terminal

## Phase 4 — Plans & deployment safety

- [ ] Saved plan (`-out`) + `show -json` parsing ([06](06-plan-apply-safety.md))
- [ ] Resource-change display (3-pane) + filters
- [ ] Sensitive-data redaction ([11](11-secrets.md))
- [ ] Plan + configuration + lock hashing; invalidation
- [ ] Apply exact saved plan
- [ ] Production confirmation (type env name)
- [ ] Destroy workflow
- [ ] Per-environment operation locks
- [ ] Drift-only (refresh-only) planning

## Phase 5 — Git core

- [ ] Repository detection · init · clone ([08](08-git-engine.md))
- [ ] Status (`--porcelain=v2 -z`) parsing
- [ ] Stage/unstage · commit · fetch/pull/push
- [ ] Git command preview on every action ([23](23-command-transparency.md))
- [ ] Branch management · history · diff viewer
- [ ] Stash · merge · conflict detection

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
