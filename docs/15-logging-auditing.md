# 15 · Logging & Auditing

Logging uses `Microsoft.Extensions.Logging`. Everything written to disk is redacted first ([11-secrets.md](11-secrets.md)). Logs live under the data root `Logs\` folder, split by type.

## Log types

**Application logs** (`Logs\application\`) — startup, shutdown, navigation errors, database errors, unhandled exceptions, configuration changes.

**Terraform logs** (`Logs\terraform\`) — command, redacted arguments, start/end time, exit code, parsed events, plan summary, error diagnostics.

**Git logs** (`Logs\git\`) — operation, repository, branch, exit code, redacted remote, error output.

**Diagnostics** (`Logs\diagnostics\`) — failure captures and exportable diagnostic bundles (secrets redacted).

## Audit events

Project created · project imported · environment created · cloud connection changed · apply started · apply completed · destroy attempted · state changed · force-unlock performed · force-push performed · settings changed.

Audit events are higher-value than raw logs: they record *who did what safety-relevant action, when, and against which project/environment*. In enterprise mode they can be centralised in SQL Server ([12-database-design.md](12-database-design.md)).

## Retention (defaults, configurable)

- Application logs: **30 days**.
- Command history: **configurable**.
- Plan summaries: retained until project removal.
- Raw temporary output: removed after successful redaction and parsing.
- Failed-operation diagnostics: retained per user policy.

## Principles

- Redact before persist, never after.
- Never write raw plan/state/output JSON to normal logs.
- Keep raw temporary command output only long enough to parse and redact it, then delete.
- Make diagnostics exportable in one click, with secrets stripped, for support.
