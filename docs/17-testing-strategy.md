# 17 · Testing Strategy

Five test tiers, each with its own project (see [02-solution-structure.md](02-solution-structure.md)).

## Unit tests

Fast, no I/O. Cover: command argument generation, path validation, environment mapping, risk classification, plan parsing, sensitive-data redaction, Git status parsing, manifest serialisation, project scanning, database configuration, cloud environment construction.

## Integration tests

Use **temporary directories and the real command-line tools**. Cover: `terraform init`, `terraform validate`, local-provider plans, saved-plan parsing, apply against harmless local resources, `git init`, commits, branch creation, merge conflicts, file-watcher behaviour, SQLite migrations.

## Contract tests

Store example output **fixtures from multiple versions** of each external interface, so parser changes are caught when a tool updates its format: Terraform JSON UI, Terraform plan JSON, Terraform validate JSON, Git porcelain output, and the Azure DevOps / GitHub / Bitbucket / GitLab APIs.

## Security tests

Command injection · malicious file paths · directory traversal · token redaction · environment-variable leakage · symlink & junction behaviour · unsafe Git URLs · production-confirmation bypass · plan substitution · modified plan files.

These map directly to the safety guarantees — e.g. "plan substitution" and "modified plan files" verify [ADR-0003](adr/0003-saved-plan-only-apply.md); "command injection" verifies the `ArgumentList` rule in [05-terraform-engine.md](05-terraform-engine.md).

## UI tests

Create a project · import a project · add an environment · edit & save files · detect external file changes · run validate & plan · review plan changes · apply a saved plan · create a Git commit · resolve a conflict · change settings.

## Definition of done (per feature)

A feature is done when it has unit coverage for its logic, integration coverage where it touches real tools, its safety-relevant paths have security tests, and the [PROGRESS.md](PROGRESS.md) checklist item is ticked with a link to the tests. See [WORKFLOW.md](WORKFLOW.md).
