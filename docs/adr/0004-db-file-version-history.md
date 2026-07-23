# ADR-0004 · Database-backed file version history for recovery

- **Status:** Accepted
- **Date:** 2026-07-23
- **Relates to:** [ADR-0002](0002-files-as-source-of-truth.md) (amended)

## Context

Users want accidental file loss — an overwrite, or a delete from Explorer/another tool — to be recoverable from inside Fenrix, independent of whether they had committed to Git. This appears to conflict with ADR-0002 ("the database is an index, not a copy of project files").

## Decision

Fenrix records a **version snapshot of every create/update** and retains the last-known content of **deleted** files in the connected database, as a **recovery cache / local history** — explicitly *not* the working copy. Rules that keep this consistent with ADR-0002:

- Disk is always read/written **first**; the snapshot is recorded **after**. Disk remains authoritative for *current* state.
- The store only supplies *previous* versions on **explicit user request** (history view / recover deleted).
- Content is **content-addressed, deduplicated, compressed**, and bounded by a **retention policy**.
- Only Terraform-relevant text files are versioned; ignored/generated/binary content is excluded.
- In-app **hard deletion of tracked files is disabled by default** (Recycle Bin + retained version instead); external deletions are detected and made recoverable.
- The store is **database-agnostic** via `IFileHistoryStore` + EF Core, working identically on SQLite or SQL Server.

## Consequences

**Positive.** Fine-grained undo/recovery between Git commits; accidental deletions are reversible; teams can centralise history on SQL Server.

**Negative / mitigations.** The database grows → dedup + compression + retention thinning + size thresholds (large files stored by reference). Stored content is real (not redacted) → protect the DB by OS/db permissions, never surface versions in logs/diagnostics, honour ignore rules so secret files opted out stay out ([11-secrets.md](../11-secrets.md)).

**Rejected alternative.** Relying on Git alone — rejected because it does not capture uncommitted saves or protect against non-Git deletions, which is exactly the loss users want covered.
