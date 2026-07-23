# 01 · Architecture

Fenrix follows Clean Architecture: dependencies point inward, the domain has no outward dependencies, and external tools (Terraform, Git, cloud CLIs, databases) sit at the edge behind interfaces.

## Layered view

```text
┌───────────────────────────────────────────────────────────┐
│                 .NET MAUI Windows Shell                    │
│  ┌─────────────────────────────────────────────────────┐  │
│  │               Blazor Hybrid UI                      │  │
│  │ Projects | Editor | Terraform | Git | Cloud | Logs  │  │
│  └─────────────────────────────────────────────────────┘  │
└───────────────────────────┬───────────────────────────────┘
                            │
┌───────────────────────────▼───────────────────────────────┐
│                    Application Layer                       │
│ Use cases | Validation | Job coordination | Safety rules   │
└───────────┬─────────────────┬─────────────────┬────────────┘
            │                 │                 │
┌───────────▼───────┐ ┌───────▼────────┐ ┌──────▼──────────┐
│ Terraform Engine  │ │   Git Engine   │ │ Project Engine  │
│ CLI + JSON parser │ │ CLI + providers│ │ Files + sync    │
└───────────┬───────┘ └───────┬────────┘ └──────┬──────────┘
            │                 │                 │
┌───────────▼─────────────────▼─────────────────▼────────────┐
│                   Infrastructure Layer                     │
│ Process runner | Filesystem | EF Core | Secrets | APIs     │
└───────────┬─────────────────────────────────────┬──────────┘
            │                                     │
┌───────────▼─────────────┐           ┌───────────▼──────────┐
│ Terraform/Git/Cloud CLI │           │ SQLite / SQL Server  │
└─────────────────────────┘           └──────────────────────┘
```

## Layers and responsibilities

**Presentation — `App` (MAUI + Blazor Hybrid).** Razor components and pages, navigation shell, theming, editor host. Holds no business logic; calls Application-layer use cases. State containers coordinate UI concerns only.

**Application.** Vertical feature slices (use cases) for Projects, Terraform, Git, Cloud, Files, Settings, Jobs, Validation. Owns orchestration, safety-policy evaluation, job coordination (one state-changing operation per environment), and validation. Depends on Domain and on abstractions, never on Infrastructure concretions.

**Domain.** Pure entities, value objects, enums, and domain rules — `InfrastructureProject`, `ProjectEnvironment`, risk classifications, plan/state models. No I/O, no framework types.

**Engines (Terraform, Git, Project).** Focused libraries that translate domain intent into CLI invocations and parse structured output back into typed results. They depend on Infrastructure abstractions (process runner, filesystem) via interfaces.

**Integrations.** Provider-specific adapters (GitHub, Azure DevOps, Bitbucket, GitLab, AWS CodeCommit) and cloud discovery (Azure, AWS, GoogleCloud). Each implements a common interface so the core stays provider-independent.

**Infrastructure.** Concrete implementations: process runner, filesystem access, EF Core persistence, secret providers, Windows-specific services, update mechanism, logging.

**Contracts.** DTOs, events, and result types shared across boundaries so layers exchange data without leaking internal models.

## Dependency rules

- Domain depends on nothing.
- Application depends on Domain + Contracts + abstractions.
- Engines/Integrations depend on Domain + Contracts + Infrastructure abstractions.
- Infrastructure depends on everything it implements, plus external SDKs/CLIs.
- Presentation depends on Application (and Contracts). It must not reach directly into Infrastructure or the engines.

Wiring is done via `Microsoft.Extensions.DependencyInjection` in `MauiProgram`, registering interfaces to concrete implementations at the composition root.

## Key runtime flows

**Run a plan.** UI → Application use case → validate project/env → resolve Terraform version, cloud connection, var-file/backend → build args safely → risk policy → confirmation → acquire environment lock → Terraform Engine runs process → parse JSON → stream to UI → persist redacted history → release lock. (Full detail in [05](05-terraform-engine.md) and [06](06-plan-apply-safety.md).)

**Edit a file.** UI editor → Application file use case → Infrastructure filesystem (atomic write) → change journal records the write → `FileSystemWatcher` recognises it as self-generated → Git status service refreshes. (See [04](04-filesystem-sync.md).)

**Git commit.** UI → Application Git use case → Git Engine builds `git` args → process runner → porcelain output parsed → typed status returned → UI updates. (See [08](08-git-engine.md).)

## Cross-cutting concerns

Logging (`Microsoft.Extensions.Logging`), configuration (`Microsoft.Extensions.Configuration`), DI, cancellation (every long-running operation is cancellable), and redaction (secrets never reach logs/history) are threaded through all layers via Infrastructure services and Application policies.

See [ADR-0001](adr/0001-drive-official-clis.md) and [ADR-0002](adr/0002-files-as-source-of-truth.md) for the two foundational decisions.
