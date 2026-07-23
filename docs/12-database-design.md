# 12 · Local Database Design

EF Core with **SQLite by default**; **SQL Server optional** for enterprise metadata. The same `AppDbContext` is configured per-provider at startup. The database is an index/cache, never a copy of project files ([ADR-0002](adr/0002-files-as-source-of-truth.md)).

## Tables

**Projects** — Id, Name, RootPath, RepositoryRootPath, Description, TerraformVersion, CreatedAt, LastOpenedAt, IsArchived.

**Environments** — Id, ProjectId, Name, WorkingDirectory, TerraformWorkspace, VariablesFile, BackendConfigFile, CloudConnectionId, IsProduction, DisplayOrder.

**Clients** — Id, Name, Code, Description, Tags. Groups connections (and optionally projects) by customer/account. See [26-connections.md](26-connections.md).

**CloudConnections** — Id, ProviderType, DisplayName, Description, ClientId, TenantOrAccountId, SubscriptionOrProjectId, Region, ProfileName, Client, SecretReferenceId, MetadataJson, Tags, IsFavorite, IsArchived, CreatedAt, LastTestedAt, LastStatus. Indexed on ClientId, ProviderType, DisplayName for fast search at scale (hundreds–thousands of connections). Query with DB-side paging — never load-all-then-filter.

**RepositoryConnections** — Id, ProviderType, DisplayName, ClientId, BaseUrl, Organisation, ProjectOrWorkspace, SecretReferenceId, Tags, IsFavorite, IsArchived.

The library is designed for **large volumes** (e.g. 500+ connections across 300 clients); the UI is virtualized and queries are indexed/paged. See [26-connections.md](26-connections.md) → Scale & organization.

**CommandRuns** — Id, ProjectId, EnvironmentId, Tool, Command, RedactedArguments, StartedAt, CompletedAt, ExitCode, Status, WorkingDirectory, OutputLogPath.

**Plans** — Id, CommandRunId, PlanFilePath, PlanHash, GitCommit, GitBranch, ConfigurationHash, AddCount, ChangeCount, DestroyCount, ReplaceCount, CreatedAt, AppliedAt, IsInvalidated.

**PlanResourceChanges** — Id, PlanId, ResourceAddress, ResourceType, ProviderName, ModuleAddress, Action, IsSensitive, SummaryJson.

**Settings** — Key, Value, Scope, UpdatedAt.

**RecentFiles** — Id, ProjectId, Path, LastOpenedAt, CursorLine, CursorColumn.

**UiLayouts** — Id, Name, LayoutJson.

**ProjectVersions** — Id, ProjectId, Label, GitCommit, GitTag, GitBranch, ConfigurationHash, ProviderLockHash, RequiredTerraformVersion, Notes, CreatedAt, CreatedBy. A version belongs to the project and can be deployed to any/all environments independently. See [20-pipelines-deployments.md](20-pipelines-deployments.md).

**Deployments** — Id, ProjectId, EnvironmentId, ProjectVersionId, PlanId, VersionLabel, GitCommit, GitBranch, ConfigurationHash, ProviderLockHash, TerraformVersion, StateBackend, StateSerial, StateLineage, Status, StartedAt, CompletedAt, InitiatedBy, Add/Change/Destroy/ReplaceCount. Each environment's current version = its latest `Succeeded` deployment; environments hold different versions simultaneously. See [20-pipelines-deployments.md](20-pipelines-deployments.md).

**FileVersions** / **FileBlobs** — content-addressed, deduplicated, compressed file-version history for recovery. Works on SQLite or SQL Server via `IFileHistoryStore`. See [21-file-history-recovery.md](21-file-history-recovery.md) and [ADR-0004](adr/0004-db-file-version-history.md).

## Notes on sensitive fields

- `RedactedArguments`, `PlanResourceChanges.SummaryJson`, and any `MetadataJson` are redacted before persistence.
- Secrets are referenced via `SecretReferenceId` → [11-secrets.md](11-secrets.md); values never live in these tables.
- `PlanFilePath`/`OutputLogPath` point at files under the data root; raw sensitive JSON is not stored.

## Provider configuration

`SQLite` is the default store under `Data\fenrix.db`. `SQL Server` (or Azure SQL) can be enabled in Settings for a central project catalogue, shared connection definitions, shared command audit, enterprise policy configuration, and organisation-wide templates.

**Important scope boundary:** a SQL Server database alone does **not** make Fenrix a multi-user remote execution platform. Centrally controlled Terraform execution requires a later **Fenrix Agent** service (Phase 11). SQL Server here is shared *metadata*, not shared *execution*.

## Migrations & backup

EF Core migrations live under `Data\migrations\`. Backups (database + manifests) live under `Backups\`. Settings exposes migration status, backup, restore, and diagnostics export ([14-settings.md](14-settings.md)).
