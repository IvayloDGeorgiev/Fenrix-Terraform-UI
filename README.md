# Fenrix IaC Studio

> Working title: **Fenrix Terraform UI** · Recommended public name: **Fenrix IaC Studio**

A modern Windows desktop application that lets DevOps engineers create, import, edit, validate, plan, deploy and manage Terraform infrastructure through a graphical interface — while the genuine Terraform and Git CLIs do the actual work underneath.

Fenrix is an **orchestration and visualisation layer** around established tools. It does not reimplement Terraform or Git; it drives them, makes them safer, and makes them easier to use.

## The three rules that govern everything

1. **Terraform and Git remain the execution engines.** Fenrix constructs and runs commands; it never parses HCL itself or reimplements state handling.
2. **Project files on disk are the source of truth.** The database is an index and cache, never a second copy of the project.
3. **No infrastructure change is applied unless the exact reviewed plan passes the safety checks.**

## Technology

.NET 10 · .NET MAUI · Blazor Hybrid · EF Core + SQLite (SQL Server optional) · Monaco editor · drives `terraform.exe`, `git.exe`, `az`, `aws`, `gcloud`. Initial target: Windows desktop.

## Documentation map

Design docs live in [`docs/`](docs/). Read them in order for a full picture, or jump to a topic.

| # | Document | Covers |
|---|----------|--------|
| — | [Overview](docs/00-overview.md) | Vision, product principles, separation of concerns |
| 01 | [Architecture](docs/01-architecture.md) | Layered architecture, dependency rules, data flow |
| 02 | [Solution Structure](docs/02-solution-structure.md) | Projects, folders, Clean Architecture boundaries |
| 03 | [Domain Model](docs/03-domain-model.md) | Projects, environments, manifest, import |
| 04 | [Filesystem Sync](docs/04-filesystem-sync.md) | Source-of-truth rules, watcher + reconciliation |
| 05 | [Terraform Engine](docs/05-terraform-engine.md) | CLI discovery, command layers, process execution |
| 06 | [Plan & Apply Safety](docs/06-plan-apply-safety.md) | Saved plans, hashing, confirmation gates |
| 07 | [Visual Resource Builder](docs/07-visual-builder.md) | Schema-driven HCL generation |
| 08 | [Git Engine](docs/08-git-engine.md) | CLI-backed Git, porcelain parsing, feature set |
| 09 | [Provider Integrations](docs/09-provider-integrations.md) | GitHub, Azure DevOps, Bitbucket, GitLab, CodeCommit |
| 10 | [Cloud Integrations](docs/10-cloud-integrations.md) | Azure, AWS, Google Cloud auth & env binding |
| 11 | [Secrets Architecture](docs/11-secrets.md) | Secret-reference model, redaction |
| 12 | [Database Design](docs/12-database-design.md) | EF Core schema, SQLite/SQL Server |
| 13 | [UI Design](docs/13-ui-design.md) | Navigation, plan review, Git pages |
| 14 | [Settings](docs/14-settings.md) | Settings model and scopes |
| 15 | [Logging & Auditing](docs/15-logging-auditing.md) | Log types, audit events, retention |
| 16 | [Error Handling](docs/16-error-handling.md) | Structured results, error classification |
| 17 | [Testing Strategy](docs/17-testing-strategy.md) | Unit, integration, contract, security, UI |
| 18 | [Packaging & Deployment](docs/18-packaging-deployment.md) | MSIX, channels, installer, associations |
| 19 | [Risks & Mitigations](docs/19-risks-mitigations.md) | Major risks and how they are addressed |
| 20 | [CI/CD Pipelines & Deployments](docs/20-pipelines-deployments.md) | Release-pipeline UI, version→environment tracking, one-click deploy |
| 21 | [File Version History & Recovery](docs/21-file-history-recovery.md) | DB-backed file snapshots, deletion recovery, DB-agnostic |
| 22 | [Terraform Files & Authoring Model](docs/22-terraform-files-model.md) | Config-vs-state model, full file-type coverage, create/update UX |
| 23 | [Command Transparency](docs/23-command-transparency.md) | Show the exact command per action; Core+Providers / any-provider |
| 24 | [Visual Design Language](docs/24-visual-design-language.md) | Modern/animated/futuristic aesthetic, tokens, motion system |
| 25 | [Execution Lifecycle & Provider Execution](docs/25-execution-lifecycle.md) | config+state→plan→execute-with-providers, worked example |
| 26 | [Connections Model](docs/26-connections.md) | Global connections library, per-environment binding, guidance |
| 27 | [Help & In-App Guidance](docs/27-help-and-guidance.md) | Help tab, contextual help, command explanations, tours, themes |

### Process & tracking

- [WORKFLOW.md](docs/WORKFLOW.md) — how we build: branching, coding standards, build/test loop, definition of done.
- [ROADMAP.md](docs/ROADMAP.md) — the 12 implementation phases, MVP scope, acceptance criteria.
- [PROGRESS.md](docs/PROGRESS.md) — living checklist of what is done, in progress, and next.
- [docs/adr/](docs/adr/) — architecture decision records.

## Current state

Fresh .NET 10 MAUI Blazor Hybrid template (multi-target: Android/iOS/MacCatalyst/Windows). Design and documentation phase. Implementation begins after the docs are agreed — see [PROGRESS.md](docs/PROGRESS.md).
