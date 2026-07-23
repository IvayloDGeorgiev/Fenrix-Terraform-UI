# 06 · Plan & Apply Safety

The centrepiece of the product. See [ADR-0003](adr/0003-saved-plan-only-apply.md).

## Plan process

```text
terraform plan
  -input=false
  -out=<generated-plan-path>
  -var-file=<environment-tfvars>
```

Then convert for review:

```text
terraform show -json <generated-plan-path>
```

The saved-plan / show-json split is the automation-style two-step workflow: the saved plan is inspected as JSON before it is applied.

## Plan review screen

Three-pane layout: **resource list | resource details | before/after comparison**, with summary cards for Add / Change / Destroy / Replace.

Display: resources to add, update, destroy, replace; output changes; data-source reads; drift; warnings; errors; provider diagnostics; resource addresses; before/after values; unknown values; sensitive-value indicators; dependency information.

Filters: Added, Updated, Destroyed, Replaced, Unchanged, Module, Provider, Resource type.

## Plan safety metadata

Persisted per plan (summary only — never raw sensitive JSON):

Project ID · Environment ID · plan file path · plan creation time · plan file hash · Git commit SHA · current branch · working-tree status · Terraform version · provider lock-file hash · configuration file hashes · cloud connection reference · counts of adds/updates/destroys/replacements · whether applied · whether configuration changed after the plan.

## Apply process

By default apply the **exact saved plan**:

```text
terraform apply -input=false <saved-plan-path>
```

Before Apply is enabled:

- Verify the plan file still exists.
- Verify its hash.
- Verify the project environment has not changed.
- Verify the selected environment.
- Verify the selected cloud account.
- Warn if the Git branch changed.
- Warn if files are uncommitted.
- Warn if the plan contains deletions.
- Warn if the plan contains replacement operations.
- Warn if the environment is marked production.

For **Live / Production**, require the user to type the environment name to confirm:

```text
Type LIVE to confirm this deployment.
```

## Plan invalidation

A plan is marked invalidated (and cannot be applied) if, after it was produced: any configuration file hash changes, the provider lock hash changes, or the Git commit changes. This prevents applying a stale or modified plan.

## Sensitive data

Plan/state/output JSON can contain sensitive values in plaintext. Fenrix parses them **in memory** where possible, redacts before persistence, and **never writes raw sensitive JSON to normal application logs**. Only redacted summaries reach the database and history. See [11-secrets.md](11-secrets.md).

## Execution lifecycle

The full config+state → plan → **execute-with-providers** lifecycle, including a worked multi-provider apply and how Fenrix surfaces per-provider/per-resource progress, is documented in [25-execution-lifecycle.md](25-execution-lifecycle.md).

## Refresh-only / drift plan

A read-only drift check runs a refresh-only plan and surfaces detected drift in the same review UI, without offering apply of infrastructure changes beyond state reconciliation.
