# ADR-0003 · Apply only the exact reviewed saved plan

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

The most dangerous thing Fenrix does is change real infrastructure. A common footgun is running `terraform apply` afresh, which recomputes a plan that may differ from what the user reviewed.

## Decision

Fenrix uses the **saved-plan two-step workflow** as the default and only path to apply:

1. `terraform plan -input=false -out=<plan> -var-file=<env.tfvars>` produces a saved plan.
2. `terraform show -json <plan>` is parsed for visual review.
3. `terraform apply -input=false <plan>` applies the **exact** reviewed plan.

Before Apply is enabled, Fenrix verifies the plan file still exists and its hash matches, the environment/cloud account is unchanged, and warns on branch changes, uncommitted files, deletions, replacements, and production targets. For Live/Production the user must **type the environment name** to confirm.

A plan is marked invalidated if configuration file hashes, the provider lock hash, or the Git commit change after it was produced.

## Consequences

**Positive.** Eliminates the "reviewed one thing, applied another" class of incident; gives every apply an auditable, hashed provenance record.

**Negative / mitigations.** Slightly less flexible than ad-hoc `apply`; users wanting a raw workflow can use the embedded terminal, which is explicitly outside the safety guarantees and labelled as such.

**Security note.** Plan/state/output JSON can contain sensitive values in plaintext → parse in memory, redact before persistence, never write raw sensitive JSON to normal logs. See [11-secrets.md](../11-secrets.md).
