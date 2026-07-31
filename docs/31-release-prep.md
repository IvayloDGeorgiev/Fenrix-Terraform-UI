# 31 · Release preparation (Phase 12)

The master checklist for turning the feature-complete build into a signed, distributable, production-ready
release. Complements [18-packaging-deployment.md](18-packaging-deployment.md),
[24-visual-design-language.md](24-visual-design-language.md), and the acceptance criteria in
[ROADMAP.md](ROADMAP.md#acceptance-criteria). Items marked **(VS)** need a build/run or signing step that must
happen in Visual Studio on a Windows box — the authoring sandbox can't compile or sign.

---

## 0 · Phase 11 close-out (do first)

Phase 11 (enterprise capability) is core-complete on `develop` — backends + UI + enforcement all landed, no new
schema beyond the already-applied `AddEnterpriseCapability` (+ `_Sql`). Two loose ends remained because the VM
was down during authoring; close them before starting the rest of Phase 12.

**0.1 Run the pure-logic cross-check.** `tests/enterprise-fixtures/verify_enterprise.py` was hand-traced but
never executed.

```
python3 tests/enterprise-fixtures/verify_enterprise.py
```

Expected: `37 passed, 0 failed`. The port mirrors `PermissionEvaluator`, `PolicyEvaluator`,
`TemplateInstantiator`, and `ApprovalResolver`; a hand-trace of all 37 assertions during this session came out
green (permission union across Global/Project/Environment scopes, policy approval/block rules incl. TF-version
constraint, template String-quoting + HCL escaping, approval separation-of-duties + expiry). Executing it is the
last confirmation.

**0.2 Build + smoke-test in VS (VS)** on `develop`, with `enterprise.json` toggled **ON** and **OFF**. Mode-off
must be byte-for-byte the prior single-user behaviour (local SQLite, allow-all, no enterprise nav group).

The five UI surfaces:

- [ ] `/enterprise/admin` — roles CRUD, permission grid, user + role-assignment management (needs `ManageRoles`).
- [ ] `/enterprise/audit` — filtered + paged audit log (needs `ViewAudit`); rows are redacted summaries only.
- [ ] `/enterprise/approvals` — approvals inbox; a non-requester with `ApproveDeployment` can approve/reject.
- [ ] Build page **Templates** tab — the template gallery instantiates through the Phase 10 authoring write path.
- [ ] Settings → **Enterprise** — read-only status (enabled, provider, organisation, current user + roles).

The governed-deploy approval path:

- [ ] With enterprise **ON** and a policy that requires approval, a deploy is gated on a role-gated
      `ApprovalRequest` (the Phase 9.5 self-ack is superseded); a separate approver must clear it.
- [ ] With enterprise **OFF**, the deploy flow falls back to the Phase 9.5 local self-ack, unchanged.
- [ ] Authorize guards fire at key-export, state ops, force-unlock, plan+destroy, and admin CRUD.

Migrations are already applied; nothing new to generate for Phase 11.

---

## 1 · UI readability + polish sweep — DONE (this session)

A systematic pass across the stylesheet + components, appended as a **Phase 12 polish** section at the end of
`wwwroot/css/fenrix.css` (overrides only — nothing above it was edited, so the diff is reviewable and
revertible). What changed and why:

- **Keyboard focus is now visible on every control.** Only inputs/selects/textareas had a focus ring before;
  buttons, tabs, rail nav, chips, rows, cells and links showed nothing when tabbed to. A `:focus-visible`
  outline (zero-specificity `:where(...)`) makes keyboard traversal legible while keeping mouse clicks ring-free.
- **Text-size floor of 11px.** Three micro-labels were sub-11px (a 9.5px sensitive-value tag, a 10px matrix-cell
  timestamp, a 10.5px apply-row provider chip) — all bumped to 11px.
- **Text-warning badges are auto-sized pills everywhere.** The base `.fx-badge` is a fixed 20×20 square for the
  single-glyph +/~/− plan chips; a `.warn` badge always carries a text label, so it was being force-wrapped one
  character per line inside the 20px box (the live bug found in `EnvironmentConnectionBar`). The
  `.fx-envconn`-scoped fix is now promoted to a global `.fx-badge.warn` rule so it can't recur on any page.
- **`--fx-faint` contrast.** Faint (hints/meta/timestamps) failed WCAG AA on both themes — worst on Light
  (~2.8:1). Nudged to ~4.5–4.8:1 while keeping the dim > faint hierarchy.
- **Narrow side-column safety.** Header rows that mix a title with chips/pickers/badges (`.fx-panel-head`,
  `.fx-deploy-head`, `.fx-stage-version`, the Outputs head, schema-row heads) now `flex-wrap` instead of
  overflowing when the Terraform / Plan & apply / Inspect / Build left columns get narrow. Long output names
  truncate with an ellipsis + `title`.

Component-level: truncated display names gained `title` tooltips (`ProjectCard`, `OutputsPanel`). Icon-only
buttons across the app already carried `title` attributes (audited — good existing hygiene), and reduced-motion
is already honoured globally (`prefers-reduced-motion` + the `data-reduced-motion` toggle disable all
animation/transition). **(VS)** verify the sweep visually in Dark **and** Light after building.

---

## 2 · MSIX packaging (VS)

The scaffold ships `<WindowsPackageType>None</WindowsPackageType>` (unpackaged) for the fast inner loop. Release
builds switch to MSIX.

- A **Windows publish profile** is provided at `Properties/PublishProfiles/win-msix.pubxml` (Release · x64 ·
  self-contained · R2R · `WindowsPackageType=MSIX`). Publish from VS (right-click → Publish → `win-msix`) or:

  ```
  dotnet publish -f net10.0-windows10.0.19041.0 -c Release -p:PublishProfile=win-msix
  ```

- **`Package.appxmanifest`** now registers file associations (`.tf` `.tfvars` `.hcl` → "Terraform
  configuration", `.tfplan` → "Terraform saved plan", `.fenrixproject` → "Fenrix project") and the
  `fenrix-iac://` protocol. These are MSIX-only and ignored by the unpackaged dev build.
- **Set a real identity before shipping (VS):** `Identity Name` (from `ApplicationId`), and **`Publisher` must
  match the signing certificate subject** exactly (e.g. `CN=Ivaylo Georgiev, O=…`). Bump
  `ApplicationDisplayVersion`/`ApplicationVersion` in the `.csproj` per release.
- **WebView2 runtime:** the installer/first-run must confirm the Evergreen WebView2 runtime is present (it ships
  with Windows 11 and current Windows 10; older machines need the bootstrapper).
- **Installer responsibilities** (docs/18): create the data root (`C:\FenrixSource\FenrixIaCStudio\`, LOCALAPPDATA
  fallback), grant the user modify access, Start-menu shortcut, preserve user projects on upgrade, never remove
  the data root on uninstall without explicit consent.

## 3 · Code signing (VS)

- Sign with an **EV or OV code-signing certificate** (EV avoids SmartScreen reputation warm-up). Store the cert
  in the machine store and reference it by thumbprint, or sign the produced package with `signtool`:

  ```
  signtool sign /fd SHA256 /a /tr http://timestamp.digicert.com /td SHA256 Fenrix.IaCStudio.msix
  ```

- The profile leaves `AppxPackageSigningEnabled=false` so unsigned local test packages build; flip it on (or use
  `signtool`) for distribution. The manifest `Publisher` and the cert subject must match or install fails.
- Timestamp every signature so packages stay valid after the cert expires.

## 4 · Update mechanism + channels

- **Channels:** Development · Internal testing · Preview · **Stable** (docs/18). Stable is the default GA channel.
- **MSIX app-installer / Store** drives channel-based auto-update. Whichever transport, the updater **must
  preserve the data root, the database, and registered projects across versions**, and let `AppInitializer` run
  pending EF migrations on first launch of the new version (docs/12). Upgrades never require a DB reset —
  `AppInitializer` migrates incrementally and adopts legacy `EnsureCreated` databases.
- Because Phase 12 now takes a **pre-migration backup on every startup** (§5), a bad migration in a new version is
  recoverable by restoring the snapshot taken just before it ran.

## 5 · Crash recovery + database backup — DONE (code; VS to build)

Implemented in Clean Architecture, new services only (no edits to the Phase 4/9.5/11 verified services):

- **`IBackupService`** (`Application/Abstractions/Maintenance`) + `BackupModels` contracts, implemented by
  **`SqliteBackupService`** (`Infrastructure/Maintenance`), registered as a singleton.
- **Backups** use SQLite's online backup API (WAL-safe while the DB is open), write to the data root's
  `Backups/` directory with a bounded retention (10), and **skip** cleanly when the metadata store is an external
  SQL Server (that's the DBA's job). Nothing sensitive lives in a backup — the DB holds only references, never
  credential values (docs/11).
- **Startup flow** (wired into `AppInitializer`, all best-effort, before the DbContext opens): apply any restore
  the user staged last session → detect an unclean prior shutdown via the session marker → take a routine
  snapshot (also captures the pre-migration state) → bring schema up to date → write a fresh session marker.
- **Clean shutdown** removes the marker via the Window `Destroying` hook in `App.xaml.cs`
  (`IBackupService.EndSession()`); if the process is killed the marker survives and the next launch reports the
  crash + offers the latest backup.
- **Restore is staged**, not live: `RestoreAsync` takes a safety copy and records a pending pointer;
  `ApplyPendingRestoreAsync` performs the file swap at the next launch before any connection can hold the DB.

**(VS) still to wire (optional, small):** a Settings → Maintenance panel calling `CreateBackupAsync` /
`ListBackups` / `RestoreAsync` so users can snapshot/restore on demand and see the crash-recovery report. The
service is UI-ready.

## 6 · Security review — spot-checked (this session)

Confirmed against the code, consistent with the documented posture (docs/11, docs/23):

- **No shell strings anywhere.** `ProcessRunner` sets `UseShellExecute = false` and passes every argument via
  `ArgumentList` (proper argv, no shell parsing). A repo-wide search found **zero** `UseShellExecute = true`. The
  `.cmd`/`.bat` CLI shims (az/aws/gcloud) are routed through `cmd.exe /c` still as argv, never a concatenated
  command line.
- **Secrets never hit the database or logs.** Only a `SecretReference` is persisted; values live in Windows
  Credential Manager (P/Invoke) or DPAPI-encrypted files outside any project. `-json` command output
  (`show`/`apply`/`output`/`state pull`) runs `captureLog:false` and is never written to a log; history rows
  store redacted arguments only.
- **Preview == execution.** The command shown to the user is built from the same `ArgumentList` that runs, so a
  preview can't hide a real argument.
- **Enterprise only ever tightens.** Governance is additive (allow-all when off); RBAC unions in-scope grants.

**(VS) recommended before GA:** run `dotnet list package --vulnerable --include-transitive` and the repo's
`/security-review` over the pending diff; confirm project repos stay **private** (plan/state files carry
plaintext secrets by design — docs/06).

## 7 · Performance checklist (VS)

- Publish with **R2R** (in the profile) and measure cold start; consider trimming once verified free of
  reflection breakage (EF + Blazor need care).
- Confirm the hand-rolled editor + DAG graph stay smooth on large files/graphs; both are dependency-free and
  reduced-motion aware.
- Spot-check large redacted-history and audit queries are paged (audit viewer pages at 100).

---

## 8 · Acceptance criteria 1–20 (ROADMAP)

| # | Criterion | Delivered by | Status |
|---|-----------|--------------|--------|
| 1 | Install on Windows | Phase 12 packaging | **At risk** — scaffolded (profile + manifest); needs a signed MSIX build **(VS)** |
| 2 | Project with Dev/UAT/Live | Phase 2 | ✅ |
| 3 | Import existing repo unchanged | Phase 2 | ✅ |
| 4 | Add a custom environment | Phase 2 | ✅ |
| 5 | Open & edit Terraform files | Phase 2 / 10.5 | ✅ |
| 6 | See external (Explorer/editor) changes | Phase 2 (FileSystemWatcher) | ✅ |
| 7 | init + validate from buttons | Phase 3 | ✅ |
| 8 | Generate a saved plan | Phase 4 | ✅ |
| 9 | Review add/mod/del/replace graphically | Phase 4 | ✅ |
| 10 | Apply the exact reviewed plan | Phase 4 (ADR-0003) | ✅ |
| 11 | Prevented from applying a stale/modified plan | Phase 4 (hash invalidation) | ✅ |
| 12 | Different cloud account per environment | Phase 8 (ADR-0005) | ✅ |
| 13 | View & manage Git changes | Phase 5 | ✅ |
| 14 | Commit, fetch, pull, push | Phase 5 | ✅ |
| 15 | Create & switch branches | Phase 5 | ✅ |
| 16 | Connect to GitHub / Azure DevOps | Phase 7 | ✅ |
| 17 | Every installed TF command via typed screen / dynamic builder / embedded terminal | Phase 3/9/10 + **Phase 12** | ✅ — typed screens for the common set, **plus** the new `-help`-driven **Commands builder** (every installed command, mutating ones redirected to their safe screens) **and** the **ConPTY embedded Terminal** (interactive catch-all). Builder is dependency-free; the terminal needs a VS build to validate the native ConPTY plumbing. |
| 18 | Review redacted execution history | Phase 3 | ✅ |
| 19 | No plaintext credentials in the DB | Phase 7/8/8.5 | ✅ (Credential Manager / DPAPI; only references persisted) |
| 20 | Reopen without losing registrations/settings | Phase 1/2 + Phase 12 backup | ✅ (hardened by crash recovery) |

**Net:** 19 of 20 met. **AC 1** is the only remaining gate — build + sign the MSIX **(VS)**. AC 17 is now
closed by the Phase 12 command builder + embedded terminal (below).

## 10 · Full Terraform coverage (Phase 12 — closes AC 17)

- **Commands builder** (`/projects/{id}/commands`): reads `terraform -help` for the full command list and each
  command's own `-help` for its flags, building a dynamic form. Runs through the one safe spine
  (`TerraformCommandKind.Custom` → catalog → runner; preview == execution; redacted history). Mutating/guarded
  commands (`apply/destroy/import/state mv|rm|push/force-unlock/workspace new|delete|select/taint/untaint/
  login/logout`) are listed but redirect to their dedicated safe screens — `TerraformCommandClassifier` is the
  single source of that rule, so ADR-0003 and per-environment locking are never bypassed. Pure `TerraformHelpParser`
  is fixture-testable. Dependency-free; **no new migration**.
- **Embedded Terminal** (`/projects/{id}/terminal`): an interactive shell under a Win32 **ConPTY** pseudo-console
  (`ITerminalService`/`ConPtyTerminalService`) with a hand-rolled, dependency-free VT renderer
  (`wwwroot/js/fenrix-terminal.js`). Windows-only; inherits ambient cloud credentials. **(VS) validate the native
  ConPTY plumbing on a build** — the P/Invoke + pipe read-loop can't be exercised in the authoring sandbox.
- Both added as ribbon tabs; Help page rewritten into a full documentation hub with a **Terraform + Git command
  cheatsheet**.

---

## 9 · User docs · example projects · stable channel

- **User guide:** `docs/user-guide.md` (getting started → first plan/apply → Git → connections).
- **Example projects:** under `examples/` for import smoke-testing and onboarding.
- **Stable channel:** cut once AC 1 (signed MSIX) lands and AC 17 is decided; tag `v1.0.0` on `develop` → merge
  to the release line.
