# ADR-0002 · Files on disk are the source of truth; the database is an index

- **Status:** Accepted
- **Date:** 2026-07-23

## Context

Fenrix manages Terraform projects that also live on the filesystem and in Git, and may be edited by Windows Explorer, VS Code, or the terminal at any time. We must decide what is authoritative.

## Decision

The **physical filesystem is authoritative.** The database (SQLite by default) is an **index and cache** only. It stores project registrations, environment mappings, settings, execution history, cached results, UI state, connection references, recent files, and plan summaries — never a mirror of `.tf`/`.tfvars`/`.hcl`/state/Git content.

Consequences of this choice:

- Writes from Fenrix are **atomic** (temp file → flush → replace) and recorded in a short-lived change journal so the watcher can distinguish self-generated changes.
- External changes are detected via `FileSystemWatcher` **plus** periodic reconciliation (watcher events can be combined, reordered, duplicated, or missed).
- Content hashes detect drift between the index and disk; conflicts prompt the user.
- Deleting files prefers the Recycle Bin over permanent deletion.

## Consequences

**Positive.** No data-loss risk from a stale database; interoperates cleanly with any external editor and with Git; existing projects can be registered in place without restructuring.

**Negative / mitigations.** Requires robust watcher + reconciliation and loop-prevention journaling (detailed in [04-filesystem-sync.md](../04-filesystem-sync.md)). The index can go stale between reconciliations → treat it as a cache and re-scan on open and on demand.

**Rejected alternative.** Importing project contents into the database as the working copy — rejected because it competes with the filesystem and Git and creates a second source of truth.

## Amendment (2026-07-23) — recovery history is compatible

[ADR-0004](0004-db-file-version-history.md) adds a database-backed **file version history** so accidental loss is recoverable. This does not violate this ADR: disk is still written first and remains authoritative for *current* state; the version store is a recovery *cache* that only supplies *previous* versions on explicit request. It is a snapshot log, not the working copy. See [21-file-history-recovery.md](../21-file-history-recovery.md).
