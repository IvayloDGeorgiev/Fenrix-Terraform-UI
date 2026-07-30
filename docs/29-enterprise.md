# 29 · Enterprise Capability (Phase 11)

How a team turns a single-user Fenrix desktop into an organisation-governed tool
**without giving up the offline, files-are-source-of-truth, drive-the-official-CLIs posture**
of the earlier phases. Phase 11 adds a shared **metadata** layer, an **identity** seam,
**role-based restrictions**, **central audit**, **shared policies + templates**, **role-gated
approvals**, and **organisation-controlled Terraform versions**. It does **not** add shared
*execution* — that is the Fenrix Agent, designed (only) in [30-fenrix-agent.md](30-fenrix-agent.md).

See also: [12-database-design.md](12-database-design.md) (SQLite/SQL Server), [15-logging-auditing.md](15-logging-auditing.md)
(audit events), [20-pipelines-deployments.md](20-pipelines-deployments.md) (approvals), [ADR-0006](adr/0006-enterprise-metadata-and-identity.md),
[ADR-0007](adr/0007-execution-host-seam.md).

## Scope boundary (read this first)

A SQL Server database alone does **not** make Fenrix multi-user remote execution. Everyone still
runs Terraform on their **own** desktop, against their **own** cloud credentials; the shared database
holds **metadata** (catalog, connections, templates, policy, audit, roles) — not a shared execution
engine. Central, agent-run execution is a later phase; the desktop is kept *agent-ready* through the
[`IExecutionHost`](adr/0007-execution-host-seam.md) seam but ships a local host only.

Enterprise features are **opt-in and additive**. With no enterprise config present Fenrix behaves
exactly as before: local SQLite, the current single-user everything-allowed posture, a local self-ack
approval. Enterprise mode never *loosens* a safety rule — RBAC and policy can only *add* gates.

## Enabling enterprise mode (bootstrap)

The database provider must be chosen **before** any row is read, so it cannot come from the Settings
table (that lives *in* the database). Enterprise configuration is therefore a small **bootstrap file**,
`enterprise.json`, in the data root (next to `fenrix.db`), read once at startup by `EnterpriseBootstrap`:

```jsonc
{
  "enabled": true,
  "metadataProvider": "SqlServer",       // "Sqlite" (default) | "SqlServer"
  "connectionStringEnvVar": "FENRIX_SQL", // name of an env var holding the connection string
  "organisation": "Contoso Platform"
}
```

The connection string itself is **not** stored in the file — the file names an **environment variable**
that holds it, so a secret is never committed to disk in plaintext (same spirit as the secret-reference
rule, [11-secrets.md](11-secrets.md)). If the var is unset or the file is absent/`enabled:false`, Fenrix
falls back to local SQLite. The active mode is surfaced read-only in Settings → Enterprise.

`AppInitializer` already brings any provider's schema up to date via EF migrations and is upgrade-safe
([12](12-database-design.md)); the SQL Server adoption path mirrors the SQLite one (create-if-missing +
stamp history) using provider-appropriate catalog queries.

## Identity

Everything auditable or governed needs a *who*. Phase 11 introduces `IUserContext` in Application,
resolved once per scope, replacing the inlined `System.Environment.UserName` used by the version and
deployment recorders. The default implementation, `WindowsUserContext`, resolves the current Windows
user (SID + `WindowsIdentity.Name`, and the UPN when domain-joined). The identity is a stable **key**
(`UserKey`, the SID) plus a display name; roles are assigned against the key in the metadata DB.

`IUserContext` is deliberately a narrow seam so **Entra ID / OIDC** can slot in later (a signed-in
identity with verified group claims) without touching any call site — see ADR-0006, "pluggable identity".
This phase does not add interactive sign-in; on a Windows desktop the OS user is a reasonable, offline,
zero-config identity, and the trust model is "the shared database is as trusted as the AD domain".

## Role-based access control

Three domain types, all in the metadata DB so a team shares one definition:

- **`OrgUser`** — a known identity (`UserKey`, display name, email, enabled flag).
- **`OrgRole`** — a named bundle of `Permission`s (a `[Flags]` enum: `ViewProjects`, `RunPlan`,
  `RunApply`, `RunDestroy`, `RunApplyProduction`, `ManageState`, `ForceUnlock`, `ManageConnections`,
  `ExportPrivateKey`, `ApproveDeployment`, `ManageTemplates`, `ManagePolicy`, `ManageRoles`, `ViewAudit`).
  Four seeded defaults: **Viewer**, **Operator**, **Approver**, **Administrator**.
- **`RoleAssignment`** — binds a user to a role at a **scope**: `Global`, a specific `Project`, or a
  specific `Environment`. Environment beats project beats global (most-specific-first, mirroring settings).

`IAuthorizationService` answers `HasPermissionAsync(permission, projectId?, environmentId?)` by unioning
the permissions of every assignment in scope. The evaluation is **pure** (`PermissionEvaluator` in
Application takes the assignments + the request and returns a boolean) so it is unit-testable without a DB.

**Enforcement** is a guard at each safety-relevant call site — `RunApply`/`RunApplyProduction` before an
apply, `RunDestroy` before destroy, `ForceUnlock` before force-unlock, `ManageState` before state ops,
`ExportPrivateKey` before a key reveal, `ManageConnections`/`ManagePolicy`/`ManageRoles`/`ManageTemplates`
before those admin actions. A denied action returns a typed "not authorised" result (never a silent no-op)
and writes an audit row. When enterprise mode is **off**, the authorization service returns `true` for
everything — the single-user posture is preserved by making "no policy" mean "allow".

## Central audit

`AuditEvent` records *who did what safety-relevant action, when, against which project/environment, and
whether it was allowed/blocked*. The event catalogue matches [15-logging-auditing.md](15-logging-auditing.md):
project created/imported, environment created, connection changed, apply started/completed, destroy
attempted, state changed, force-unlock, force-push, settings changed, **plus** the Phase 11 additions
(role changed, policy changed, template applied, approval requested/decided, private-key export,
authorisation denied). `IAuditSink.WriteAsync(AuditEvent)` persists a **redacted** row to the metadata DB
(so a team sees one central trail); it is best-effort and never blocks or fails the underlying action.
A paged, filterable **Audit** viewer (by user, project, action, outcome, date) reads it back. Audit rows
are append-only and hold only summaries/identifiers — never secrets, plan JSON, or key material.

## Shared policies

`OrgPolicy` is a small set of org-wide switches enforced **in addition to** every existing gate — it can
only *tighten*. Covered this phase:

- `RequireApprovalForProduction` / `RequireApprovalForEnvironments` — force the approval gate on Live (or a
  named set), replacing the local self-ack.
- `AllowedTerraformVersions` — an allow-list / minimum constraint; a disallowed binary blocks plan/apply
  (see "Org-controlled Terraform versions").
- `RequiredBranchForProduction` — Live may only deploy from an approved branch.
- `BlockProductionDestroy` — destroy against a production environment is refused outright.
- `RequirePrivateRepositories` — warns/blocks binding a public repo (plans/state carry secrets, [06](06-plan-apply-safety.md)).

Policy evaluation folds into the **existing** `DeploymentGateEvaluator` and the apply **preflight** rather
than adding a parallel path — a policy failure surfaces as one more blocking gate/preflight check with a
clear reason. `IPolicyService` reads the single active `OrgPolicy` (cached per scope). With enterprise mode
off there is no policy row and nothing is added.

## Shared templates

The reusable templates deferred from Phase 10 live here so a team shares one library. A `ConfigTemplate`
is a named, parameterised HCL scaffold (`TemplateParameter`s with type + default + description); instantiation
is **pure** — parameters are substituted and the result is emitted through the **existing** Phase 10
`ConfigHclBuilder` / `HclEmitter` and written via the **same atomic-write + file-history path** as every
other authored file (`IFileTreeService.WriteFileAsync`). Templates are stored in the metadata DB (shared when
enterprise mode is on; local SQLite otherwise), managed behind `ManageTemplates`, and surfaced as a gallery
in the **Build** page with an apply-with-parameters flow. Templates author **config only**, never state —
the ADR-0002 boundary is unchanged.

## Approval workflows

Phase 9.5 shipped an approval **gate** with a *local self-ack* (a checkbox the operator ticks themselves)
and left real multi-user approval for this phase. Phase 11 replaces the self-ack with a role-gated
`ApprovalRequest`: when a stage/policy requires approval, the deploy creates a pending request naming the
required permission (`ApproveDeployment`) and scope; a **different** user holding that permission records an
`ApprovalDecision` (approve/reject + comment) from an **Approvals inbox**; only an approved, still-valid
request lets the governed apply proceed. The approver may not be the requester (separation of duties). The
request captures the exact version/commit/plan so approval is of a specific, unchangeable deploy. Everything
is recorded in audit. Nothing here bypasses [ADR-0003](adr/0003-saved-plan-only-apply.md) — approval is a
gate *before* applying the exact saved plan.

## Org-controlled Terraform versions

`OrgPolicy.AllowedTerraformVersions` (an allow-list and/or a minimum `required_version`-style constraint) is
checked by the existing Terraform discovery/constraint layer: the resolved binary's version is matched against
the org constraint at plan/apply time, and a disallowed version blocks with guidance (which versions are
permitted) rather than silently proceeding. This reuses the Phase 3 `TerraformVersionConstraint` grammar; the
org constraint is simply an *additional* constraint AND-ed with the project's own `required_version`.

## What Phase 11 deliberately does not do

- **No shared execution.** Runs stay on the user's machine; see [30-fenrix-agent.md](30-fenrix-agent.md).
- **No interactive sign-in.** Windows identity only; Entra/OIDC is a future drop-in via `IUserContext`.
- **No secret centralisation.** Secrets remain local (Credential Manager / DPAPI); the shared DB holds only
  references, exactly as [11-secrets.md](11-secrets.md) mandates. A team member resolves their own secrets.
- **No loosening.** Enterprise mode only ever *adds* gates; with it off, behaviour is byte-for-byte the prior
  single-user experience.

## Migrations

Phase 11 adds the metadata tables (`OrgUsers`, `OrgRoles`, `RoleAssignments`, `AuditEvents`, `OrgPolicies`,
`ConfigTemplates`, `TemplateParameters`, `ApprovalRequests`) as **one SQLite migration** (`AddEnterpriseCapability`)
**and** a parallel **SQL Server** migration set (the provider is selected by the design-time factory /
bootstrap). Generate both in Visual Studio — see PROGRESS.md → Phase 11 for the exact commands. The tables are
provider-agnostic (no SQLite-only column types); the `DateTimeOffset`-as-binary converter stays SQLite-only
so SQL Server uses its native `datetimeoffset`.
