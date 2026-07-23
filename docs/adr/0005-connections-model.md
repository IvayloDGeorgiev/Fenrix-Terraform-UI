# ADR-0005 · Connections: global library + per-environment binding

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

Every project must authenticate to a cloud account (and a Git host) to run Terraform. The open question was where connection details live: a single global section, per project, or per environment. Different environments (Dev/UAT/Live) typically target different accounts/subscriptions, and users don't want to re-enter credentials for every project.

## Decision

Use **two layers** for cloud connections:

1. **Global Connections library** — connections are defined once (provider, client/account, region, description + a secret *reference*) and reused across all projects and environments. Surfaced as a top-level **Connections** hub.
2. **Per-environment binding** — each environment references exactly one cloud connection (`ProjectEnvironment.CloudConnectionId`). This is the **only** place a cloud connection is bound. Environments target different accounts by design.

**The project holds no cloud connection.** Fenrix deploys/updates/manages per environment, so the account is always chosen at the environment level — there is no project-wide cloud connection to inherit or override. A creation-time "apply one to all" shortcut only *pre-fills* each environment's own binding; it is not stored on the project.

The **repository** connection is separate and bound per project (a project maps to one Git repo). Creation-time guidance requires selecting a cloud connection per environment; environments without one are flagged and blocked from state-changing operations until bound. Secrets are never stored on the connection — only a reference ([11-secrets.md](../11-secrets.md)).

## Consequences

**Positive.** Reuse without re-entry; correct per-environment isolation (dev vs prod accounts); clear guidance prevents "forgot to pick a connection"; connection identity is visible in previews and deployment records, never the secret.

**Negative / mitigations.** The user must choose a connection per environment rather than once per project → mitigated by the creation-time "apply one to all" pre-fill for the common case, while keeping each environment independently editable. Deleting an in-use connection could orphan environments → the hub tracks usage references and warns/blocks deletion.

**Rejected alternatives.** *Project-owned* cloud connection (breaks environment isolation — deploys happen per environment, so the account must be per environment); *environment-only* with no library (forces credential re-entry and duplication across projects).
