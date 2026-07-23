# 26 · Connections Model

**The connection lives on the environment, not the project.** A project has **no cloud connection of its own** — because Fenrix manages, deploys, and updates infrastructure **per environment**, and each environment authenticates to its own account. Fenrix provides a **global Connections library** (define once, reuse) and each **environment** binds to the connection it should run against.

> **Decision (open question resolved).** *"Global section vs per-project vs per-environment?"* → **A reusable global library + a per-environment binding.** The **project does not hold a cloud connection**; the binding is on each environment (`ProjectEnvironment.CloudConnectionId` — [03](03-domain-model.md)). Recorded as [ADR-0005](adr/0005-connections-model.md).

## Why per-environment (never per-project)

The whole point of environments is isolation: Dev deploys to a dev subscription, Live to the production account. All deploy/update/manage operations run **against an environment**, so the account they use must be chosen **at the environment level**. There is no project-wide cloud connection to override or fall back to — every environment names its own. The library exists to make that per-environment choice fast and consistent.

## Two layers

1. **Connections hub (global library).** A top-level **Connections** area ([13](13-ui-design.md) nav) listing every connection the user has defined, across kinds (cloud accounts, Git/repository hosts, and future kinds). Each entry stores identifying **metadata** and a **secret reference** — never the secret itself ([11](11-secrets.md)). Reused across all projects and environments.
2. **Per-environment binding.** Each environment references exactly one **cloud** connection (the account its Terraform runs against). Different environments → different connections, freely. This is the *only* place a cloud connection is bound.

```text
Connections hub (global, reusable)
        │  define once: provider, client/account, region, description, secret ref
        ▼
   Environments bind directly (project holds NO cloud connection):
        ├── Dev   → cloud connection A   (dev subscription)
        ├── UAT   → cloud connection B   (test subscription)
        └── Live  → cloud connection C   (prod account)   ← different accounts by design
```

**Repository connection** is separate: a project maps to a single Git repo, so the *repository* connection is bound at the project level. Only the **cloud** connection is strictly per-environment. (If a repo is authenticated per Git remote via the credential helper, even this is effectively a reference, not a project-owned secret — [08](08-git-engine.md), [11](11-secrets.md).)

**Creation convenience, not a project binding.** When a user creates several environments at once, the wizard offers to pick one connection and **apply it to all** as a time-saver — but this simply *pre-fills each environment's own binding*; it is **not** stored as a project-level connection and each environment remains independently editable.

## Connection model

Cloud and repository connections already exist as distinct records ([12](12-database-design.md)); the hub is a unified view over them. Shared shape:

```csharp
public sealed class Connection            // conceptual view over CloudConnections + RepositoryConnections
{
    public Guid Id { get; init; }
    public ConnectionKind Kind { get; init; }          // Cloud | Repository | (future: e.g. Vault)
    public string ProviderType { get; init; } = "";    // Azure, AWS, GoogleCloud, GitHub, AzureDevOps, ...
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }

    // identifying metadata (no secrets) — which subset applies depends on ProviderType
    public string? TenantOrAccountId { get; set; }     // Azure tenant / AWS account / GCP org
    public string? SubscriptionOrProjectId { get; set; }
    public string? Region { get; set; }
    public string? ProfileName { get; set; }           // AWS profile / gcloud config
    public string? Client { get; set; }                // e.g. service-principal / client id (identifier only)
    public string? BaseUrl { get; set; }               // repo host / self-managed endpoint
    public string? Organisation { get; set; }          // org / workspace
    public string MetadataJson { get; set; } = "{}";   // provider-specific extras

    public Guid? SecretReferenceId { get; set; }        // pointer to secure storage — never the secret

    // organization at scale (see "Scale & organization")
    public Guid? ClientId { get; set; }                 // owning client/customer/account group
    public List<string> Tags { get; set; } = [];        // free-form labels (env class, cost centre, team…)
    public bool IsFavorite { get; set; }
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public ConnectionStatus LastStatus { get; set; }    // Untested | Ok | Failed
}
```

Metadata is exactly what the user described: provider, client, account/subscription, region, description — enough to **identify** the connection at a glance — while the actual credential stays in the tool-native or Windows secure store via `SecretReferenceId` ([10](10-cloud-integrations.md), [11](11-secrets.md)).

## Scale & organization (hundreds → thousands of connections)

The library must comfortably hold **many connections** — e.g. **500+ connections across 300 clients**, spanning multiple cloud providers and projects. There is **no fixed limit**; the design assumes large volumes from day one.

**Clients / groups.** A first-class **Client** (customer/account group) organizes connections by who they belong to. A client can own many connections (e.g. Client A's Azure prod, Azure dev, AWS audit, GitHub org). The hub's primary view can group by client, then provider.

```csharp
public sealed class Client
{
    public Guid Id { get; init; }
    public string Name { get; set; } = "";
    public string? Code { get; set; }          // short code / account number
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
}
```

**Finding a connection fast:**

- **Indexed search** across name, provider, client, account/subscription id, region, tags, and description — instant type-ahead, backed by DB indexes on the hot columns (ClientId, ProviderType, DisplayName).
- **Filters & facets:** by client, provider, kind (cloud/repo), region, tag, test status, favorite, archived.
- **Grouping & sorting:** group by client or provider; sort by name, last used, last tested.
- **Favorites & recents** for the handful a user touches daily; **archive** for retired connections (hidden by default, never hard-deleted while referenced).

**Performance:**

- The list is **virtualized** (render only visible rows) and **paged/lazy-loaded**, so 5,000 connections scroll as smoothly as 5.
- Queries are server-side/DB-side (EF Core with paging + indexes), never "load all into memory then filter" — this matters equally on SQLite and SQL Server ([12](12-database-design.md)).
- Connection **test runs are on-demand or batched**, never a blocking sweep of the whole library.

**Per-environment picker at scale.** The environment connection dropdown is a **searchable, filterable picker** (by client/provider/tag), pre-scoped to the project's client where known, so choosing among hundreds is one search, not a long scroll. "+ New connection" stays inline.

**Bulk operations:** import/export connection definitions (metadata only, never secrets), bulk-tag, bulk-assign to a client, and bulk-test — essential when onboarding a client with many accounts at once.

**Enterprise sharing.** With the SQL Server metadata option, the connections library (definitions + references, still no secrets) can be **shared across a team**, so 300 clients are maintained centrally rather than per machine ([12](12-database-design.md), Phase 11).

## Guidance & validation (so a connection is never forgotten)

- **At project creation**, the wizard asks the user to pick a connection **for each environment**. A clear message states: *"Select a cloud connection for each environment — Fenrix uses it to authenticate Terraform for that environment."* An optional **"apply one to all"** shortcut pre-fills every environment's binding at once (still individually editable). An inline **"+ New connection"** shortcut opens the hub without leaving the wizard.
- If no connection exists yet, the wizard guides the user to **create one first** (or lets them proceed and bind later, with the environment flagged).
- **An environment with no connection** shows a persistent warning badge, and any **state-changing operation is blocked** until one is selected — surfaced as an `authentication required` / `prerequisite missing` error class ([16](16-error-handling.md)), not a silent failure.
- **Test before use:** the hub and the wizard offer "Test connection" (`ICloudConnectionProvider.TestAsync` — [10](10-cloud-integrations.md)), recording `LastTestedAt`/`LastStatus`.

## How it flows into execution

When a command runs for an environment, Fenrix resolves that environment's bound connection and calls `BuildEnvironmentAsync` to compose the process-scoped credentials **at execution time** ([10](10-cloud-integrations.md), [25](25-execution-lifecycle.md)). The connection's identity (e.g. `aws:acct-123/eu-west-1`) appears in the command-preview context chips ([23](23-command-transparency.md)) and in the deployment record ([20](20-pipelines-deployments.md)) — so it's always visible which account a change is going to, and never a secret value.

## UI summary

- **Connections hub** (top-level): virtualized list scaling to thousands; indexed search + facet filters (client, provider, kind, region, tag, status); group by client/provider; favorites, recents, archive; add/edit/delete; test (on-demand/batched); bulk tag/assign/test and import/export (metadata only); see which projects/environments use each connection (usage references prevent accidental deletion of an in-use connection).
- **Project creation / Project Settings → Environments**: a per-environment connection dropdown (from the library) + "+ New connection" for each environment, with an optional "apply one to all" pre-fill; the project's repository connection is set separately.
- **Environment badge**: shows the bound connection and its test status everywhere the environment is shown ([13](13-ui-design.md)).

## Delivery placement

The Connections hub, per-environment binding, and creation-time guidance land with **Phase 8 (Cloud connections)**, with the repository-connection side aligned to **Phase 7**. The per-environment binding field itself exists from **Phase 2** (environments), so early phases can persist a chosen connection even before the full hub UI is built. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
