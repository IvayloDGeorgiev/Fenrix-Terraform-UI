# ADR-0006 · Enterprise metadata store, identity, and RBAC

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Phase 11 turns the single-user desktop into an organisation-governed tool: a shared metadata store, an
identity for auditing/authorisation, role-based restrictions, central audit, shared policy/templates, and
role-gated approvals ([29-enterprise.md](../29-enterprise.md)). Three questions had to be settled up front:
(1) how a team shares metadata, (2) where user identity comes from, and (3) how governance is enforced without
regressing the offline single-user experience.

## Decision

**Dual-provider metadata store.** The same `AppDbContext` runs on **SQLite** (default, per-user, offline) or
**SQL Server / Azure SQL** (opt-in, shared). The provider is chosen at startup from a **bootstrap file**
(`enterprise.json` in the data root) because the provider must be selected before any row — including the
Settings table — can be read. The file names an **environment variable** that holds the connection string, so
no connection secret is written to disk (mirrors the secret-reference rule, [11-secrets.md](../11-secrets.md)).
Absent/`enabled:false`/unset var ⇒ local SQLite. The `DateTimeOffset`-to-binary converter stays **SQLite-only**;
SQL Server uses its native type. `AppInitializer` stays upgrade-safe on both providers.

**Windows identity now, pluggable later.** A narrow `IUserContext` (Application) yields a stable `UserKey`
(Windows SID) + display name, replacing inlined `Environment.UserName`. `WindowsUserContext` (Infrastructure)
is the only implementation this phase. The seam is deliberately minimal so **Entra ID / OIDC** can replace it
later — verified org identity + group claims — without touching call sites. No interactive sign-in this phase.

**Additive RBAC that only tightens.** `OrgUser` / `OrgRole` (a `[Flags] Permission` bundle) / `RoleAssignment`
(scoped Global → Project → Environment, most-specific-first). `IAuthorizationService` unions in-scope
permissions; the core decision is a **pure** `PermissionEvaluator` (unit-testable without a DB). Guards sit at
each safety-relevant call site (apply/destroy/state/force-unlock/key-export/admin actions) and return a typed
"not authorised" result plus an audit row. **When enterprise mode is off, authorization returns `true` for
everything** — "no policy" means "allow", so the prior single-user posture is byte-for-byte preserved. Central
audit (`AuditEvent` + `IAuditSink`) and shared policy/templates persist to the same metadata DB.

## Consequences

**Positive.** One shared catalog/policy/audit/role definition for a team; correct per-scope authorisation;
a central, tamper-evident audit trail; identity is zero-config on Windows; the design is Entra-ready and
agent-ready ([ADR-0007](0007-execution-host-seam.md)); nothing regresses for the solo user.

**Negative / mitigations.** SQL Server can't be compiled/migrated in the authoring sandbox → a parallel
SQL Server migration set is generated and verified in Visual Studio, and the schema is kept provider-agnostic.
Windows identity is weaker than verified sign-in (a shared DB is only as trusted as the AD domain) → mitigated
by the `IUserContext` seam for a later OIDC upgrade, and by keeping enterprise mode explicitly opt-in.
Bootstrap-file config is outside the Settings UI → surfaced read-only in Settings → Enterprise and documented.

**Rejected alternatives.** *Connection string in the Settings table* (impossible — needed before the DB is
read); *connection string in the bootstrap file itself* (writes a secret to disk); *identity = a Fenrix-managed
local account* (reinvents identity, weakest trust); *a separate governance micro-service now* (that is the
Agent — deferred, ADR-0007); *making enterprise mode change defaults* (rejected — it must only ever add gates).
