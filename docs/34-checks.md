# 34 · Checks — static analysis & cost (Phase 13)

A standalone, read-only **Checks** screen that runs best-of-breed external tools over an environment's working
directory and surfaces the results in one place:

- **Static analysis** — **TFLint** (lint, deprecations, provider best-practice) plus a security-misconfiguration
  scan with **Trivy** (`trivy config`) or, if Trivy isn't installed, **tfsec**. Findings are normalised
  (severity, rule, message, file/line, resource, docs link) and shown in one filterable list.
- **Cost estimation** — **Infracost** `breakdown` (projected monthly cost + per-resource) and `diff` (the delta a
  change introduces, versus a saved baseline).

Each tool is discovered like the Terraform binary (configured path → PATH), and can be installed with one click
when missing — exactly like the Phase 12 Terraform installer.

## What it does

- **Tool status ribbon** — shows each tool as installed (with version) or missing, with a one-click **Install**
  that downloads the official release for the current OS/arch, verifies its published checksum when one is
  available, drops the binary in the shared `<dataRoot>\Tools\<tool>\` folder, and sets
  `checks.<tool>.executable` at **Global** scope so every project resolves it.
- **Static analysis tab** — runs whichever tools are installed, aggregates findings, and lets you filter by
  severity ("this level and above"). Per-tool run chips distinguish *not installed* / *error* / *N findings*.
- **Cost tab** — prompts clearly for the free Infracost API key when absent (stored in the secret store, never
  plaintext on disk), then shows the projected monthly total, an optional diff-vs-baseline delta, the
  per-resource table, and a count of resources Infracost couldn't price. "Save baseline" snapshots the current
  breakdown for later diffs.

## Design

Clean Architecture, reusing the existing spine end-to-end — no new process primitive, no new secret backend.

- **Contracts** (`Contracts/Checks/`): `CheckTool`, `CheckSeverity` (normalised, ordered), `CheckFinding`,
  `CheckToolRun`, `StaticAnalysisReport`; `CheckToolStatus` + `CheckToolInstallResult`; `CostResource` +
  `CostEstimate`.
- **Application** (`Application/Abstractions/Checks/` + `Application/Checks/`): the interfaces
  `ICheckToolDiscovery` / `IStaticAnalysisService` / `ICostEstimationService` / `ICheckToolInstaller`, and the
  **pure** parsers `TfLintJsonParser`, `TfsecJsonParser`, `TrivyJsonParser`, `InfracostJsonParser` +
  `CheckSeverityMap`. Parsers are side-effect free and defensive (tolerant of missing fields / non-JSON banners),
  so they're fixture-testable without a build.
- **Infrastructure** (`Infrastructure/Checks/`): `CheckProcessRunner` (the checks equivalent of the Terraform
  process coordinator — runs a tool through the shared `IProcessRunner` and **captures stdout/stderr in memory**
  via a synchronous `IProgress` to avoid the `Progress<T>` sync-context race), `CheckToolMetadata` (the one-place
  tool table), `CheckToolDiscovery`, `StaticAnalysisService`, `CostEstimationService`, `CheckToolInstaller`.
- **UI**: `Components/Pages/Checks.razor` at `/projects/{id}/checks`, a new **Checks** ribbon tab
  (`shield-check`). Phase 13 CSS appended to `fenrix.css`; one new `dollar` icon.

### Safety posture (same rules as the rest of the app)

- **Never a shell string.** Every tool runs through the shared `ArgumentList`-only runner (`ProcessStartRequest`),
  the same primitive the Git and Terraform engines use.
- **Output is never logged.** Check output can echo configuration values, so `CheckProcessRunner` keeps it in
  memory and only normalised findings / cost figures are surfaced — no `Logs/` file, no history row (mirrors the
  `captureLog:false` posture for `-json` Terraform commands).
- **The Infracost API key is a secret, not a setting.** It lives in the Windows Credential Manager via the
  existing `ISecretStore` (target `Fenrix:checks:infracost`); the DB/settings hold nothing. It is injected as the
  `INFRACOST_API_KEY` **environment variable** only at run time and is never placed in args, history, or logs.
  The UI prompts for it clearly instead of failing silently.
- **Read-only and standalone.** Checks takes no environment lock, records no `Deployment`, and does not touch the
  Phase 4/9.5/11 verified services. It only reads the working directory (and, for a cost baseline, writes a JSON
  snapshot under `<dataRoot>\Cache\infracost\`).

### Tools & commands

| Tool | Command Fenrix runs | Parsed from |
|------|---------------------|-------------|
| TFLint | `tflint --format json` | stdout `issues[]` / `errors[]` |
| Trivy | `trivy config . --format json --quiet` | stdout `Results[].Misconfigurations[]` (non-`PASS`) |
| tfsec | `tfsec . --format json --no-colour` | stdout `results[]` |
| Infracost (breakdown) | `infracost breakdown --path . --format json` | stdout / `--out-file` JSON |
| Infracost (diff) | `infracost diff --path . --compare-to <baseline> --format json` | stdout |

Severity is normalised onto `Critical > High > Medium > Low > Info` (`CheckSeverityMap`): scanner
`CRITICAL/HIGH/MEDIUM/LOW` map straight across; TFLint `error → High`, `warning → Medium`, `notice/info → Info`.

## Not folded into apply preflight (proposal)

Per the working agreement, Checks ships **standalone** and is deliberately **not** wired into the Phase 4 apply
preflight or the Phase 9.5/11 governed deploy. A natural follow-up — to **propose before building** — is an
opt-in gate: e.g. an org policy / per-project setting "block apply on Critical/High security findings" or
"require a cost estimate under $X", evaluated as a non-blocking warning first, then an enforced gate. That would
touch the verified `TerraformApplyService.PreflightAsync` and `DeploymentGateEvaluator`, so it is left as a
separate, reviewable change rather than folded in here.

## Build / migration

- **No new NuGet packages.** `System.Formats.Tar` (Infracost's `.tar.gz`), `System.IO.Compression`,
  `System.Security.Cryptography`, and `IHttpClientFactory` (already registered) cover the installer.
- **No new DB migration.** No schema change — findings and cost are computed on demand; the only persisted
  artefacts are the on-disk tool binaries and the optional cost baseline JSON.
- **Not compiled here (sandbox VM was down):** build on `develop` and smoke-test the Checks screen. The parsers
  were written against the tools' documented JSON shapes and are hand-traced; validate against real output from
  installed `tflint` / `trivy` / `tfsec` / `infracost`. The installer's asset-selection is version-agnostic
  (matches windows + arch tokens against the GitHub *latest release* asset list), but confirm the resolved asset
  names on a real run.
