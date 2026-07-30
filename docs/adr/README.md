# Architecture Decision Records

Short, dated records of significant architectural decisions. Each captures context, the decision, and its consequences. New ADRs are numbered sequentially and never edited once accepted — a superseding ADR is added instead.

| # | Title | Status |
|---|-------|--------|
| [0001](0001-drive-official-clis.md) | Drive the official CLIs; do not reimplement Terraform or Git | Accepted |
| [0002](0002-files-as-source-of-truth.md) | Files on disk are the source of truth; the database is an index | Accepted |
| [0003](0003-saved-plan-only-apply.md) | Apply only the exact reviewed saved plan | Accepted |
| [0004](0004-db-file-version-history.md) | Database-backed file version history for recovery | Accepted |
| [0005](0005-connections-model.md) | Connections: global library + per-environment binding | Accepted |
| [0006](0006-enterprise-metadata-and-identity.md) | Enterprise metadata store, identity, and RBAC | Accepted |
| [0007](0007-execution-host-seam.md) | Execution-host seam (agent-ready, agent deferred) | Accepted |

## Template

```markdown
# ADR-NNNN · Title

- **Status:** Proposed | Accepted | Superseded by ADR-XXXX
- **Date:** YYYY-MM-DD

## Context
What forces are at play; why a decision is needed.

## Decision
What we decided to do.

## Consequences
Positive outcomes, negative trade-offs and their mitigations, rejected alternatives.
```
