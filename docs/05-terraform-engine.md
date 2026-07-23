# 05 · Terraform Engine

Drives `terraform.exe`, parses its structured output, and streams progress to the UI.

## Binary management

Fenrix supports: using Terraform from `PATH`; selecting a custom executable; maintaining multiple versions; pinning a version per project; detecting the required version from configuration; showing installed/selected versions; validating with `terraform version`; and **refusing to run** when the selected version violates the project's constraint.

## Command transparency

Every command surface shows a **live preview of the exact command that will run** (executable + `ArgumentList` + working dir + context chips, secrets redacted). The Run button executes exactly what the preview shows — both are generated from the same argument list, so there is never divergence. This is a cross-cutting product promise detailed in [23-command-transparency.md](23-command-transparency.md).

## Two command layers

Terraform's command set changes between versions, so Fenrix has two layers plus a terminal:

### 1. Typed command screens

Dedicated graphical screens for common commands: Init, Validate, Format, Plan, Apply, Destroy, Test, Import, Output, Show, Graph, Providers, Modules, Workspace, State, Force-unlock, Refresh-only plan, Version.

### 2. Dynamic command builder

At startup or after a version change: run `terraform -help` to discover commands; run `<command> -help` when needed; cache command metadata by Terraform version; display each command and its raw arguments; allow execution from a graphical builder. This gives Fenrix support for every command the installed binary exposes without waiting for a Fenrix release.

### 3. Embedded terminal

Some commands are interactive (`terraform console`, `terraform login`, prompts, future commands, deep troubleshooting). Provide an integrated terminal backed by **Windows pseudoconsole (ConPTY)** support. The terminal is explicitly outside the saved-plan safety guarantees and is labelled as such.

## Command request

```csharp
public sealed record TerraformCommandRequest(
    Guid ProjectId,
    Guid EnvironmentId,
    string ExecutablePath,
    string WorkingDirectory,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    TerraformRiskLevel RiskLevel,
    bool RequiresInteractiveTerminal);
```

## Process execution

Use `ProcessStartInfo` with `UseShellExecute = false`, redirected stdout/stderr (and stdin where required), an explicit working directory, **`ArgumentList` rather than concatenated shell strings**, process-scoped environment variables, cancellation support, process-tree termination, and structured output events.

**Never** construct a command as one unescaped string passed through `cmd.exe`. This is both a correctness and a security requirement (see [11-secrets.md](11-secrets.md) and [17-testing-strategy.md](17-testing-strategy.md) → command injection tests).

## Execution pipeline

```text
User action
  → Validate project and environment
  → Resolve Terraform version
  → Resolve cloud connection
  → Resolve variables and backend configuration
  → Build arguments safely
  → Evaluate risk policy
  → Request confirmation where required
  → Acquire environment operation lock
  → Execute Terraform process
  → Parse JSON or text output
  → Stream progress to UI
  → Save redacted execution history
  → Release operation lock
```

## Concurrency

Allow **only one state-changing Terraform operation per environment**. State-changing commands: Apply, Destroy, Import, state modifications, force-unlock, workspace modifications, and refresh operations that write state. Read-only commands may run concurrently where safe. The lock is per-environment, coordinated in the Application layer (Jobs).

## Output parsing

Prefer JSON: `plan`/`apply`/`refresh`/`test` streaming events (the machine-readable UI log stream via `-json`), `terraform show -json <plan>`, `terraform validate -json`, `terraform providers schema -json`. Parse versioned formats, ignore unknown minor-version fields, reject unsupported majors, and keep fixtures from multiple versions for contract tests. When JSON is unavailable, fall back to text and the raw terminal.

## References (verified against official docs)

Terraform specifics in this doc and in [06](06-plan-apply-safety.md), [07](07-visual-builder.md), and [25](25-execution-lifecycle.md) were checked against HashiCorp's documentation (July 2026):

- Saved-plan workflow — a plan file from `terraform plan -out` is the input to `terraform apply`: <https://developer.hashicorp.com/terraform/cli/commands/plan>, <https://developer.hashicorp.com/terraform/cli/commands/apply>
- `terraform show -json <planfile>` → JSON representation of plan/config/state: <https://developer.hashicorp.com/terraform/cli/commands/show>
- Streaming machine-readable UI (`-json` on `plan`/`apply`): <https://developer.hashicorp.com/terraform/internals/machine-readable-ui>
- Plan/state JSON format (structures, sensitive values): <https://developer.hashicorp.com/terraform/internals/json-format>
- `terraform validate -json` (`valid`, `error_count`, `warning_count`, `diagnostics`, `format_version` "1.0"): <https://developer.hashicorp.com/terraform/cli/commands/validate>
- `terraform providers schema -json` (attributes, `block_types`/nested blocks, `required`/`optional`/`computed`, `format_version`): <https://developer.hashicorp.com/terraform/cli/commands/providers/schema>
