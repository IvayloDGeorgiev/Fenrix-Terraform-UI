# 20 · CI/CD Pipelines & Deployments

A first-class **Pipelines** area that gives Fenrix an Azure DevOps–style *release pipeline* experience: a clear board of environments, the version deployed to each, and a governed one-click promotion from environment to environment. Because Fenrix already owns Git and the saved-plan workflow, every deployment is tied to an exact, auditable version of configuration, plan, and state.

> **Scope note.** This builds on, and stays consistent with, the three rules ([00-overview.md](00-overview.md)): Terraform/Git remain the engines, files are the source of truth, and nothing applies unless the exact reviewed plan passes the safety checks. "One-click deploy" means *one click to start a governed, saved-plan apply* — not a bypass of the safety gates. Local one-click deploys run on the desktop; fully automated/central runs require the **Fenrix Agent** (Phase 11, [12-database-design.md](12-database-design.md)).

## What the user asked for

- A **Pipelines** tab with a deployment/release-pipeline UI similar to the Azure DevOps release UI.
- Clear visibility of **which environment currently runs which version**.
- Leverage the built-in version control to show **which version of the state/plan** is deployed.
- Ability to author **every part of a Terraform project visually** (resources, modules, variables, outputs, backends) and, once saved, **select an environment and deploy with one click**.
- **Independent version-per-environment.** Each project has **many versions**, and each environment runs whatever version is assigned to it — independently. For example: **v1 on Live, v1.5 on UAT (testing), v2 on Dev (in development)**, plus any custom environments. The same version can be deployed to all environments when the use case calls for it.

## Project versions (per-project, environment-independent)

A **version belongs to the project, not to an environment.** Versions are candidates that can each be deployed to any, all, or none of the project's environments — independently and in any combination.

A version is anchored to an exact, immutable snapshot of the config (a Git commit, optionally tagged with a semantic label like `1.0`, `1.5`, `2.0`). Because files are the source of truth ([ADR-0002](adr/0002-files-as-source-of-truth.md)) and Git is the version control, "a version" is a Git ref plus metadata — nothing is copied or duplicated.

```csharp
public sealed class ProjectVersion
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }

    public string Label { get; init; } = "";        // e.g. "1.0", "1.5-rc", "2.0-dev" (free-form or semver)
    public string GitCommit { get; init; } = "";     // immutable snapshot of the config
    public string? GitTag { get; init; }             // optional annotated tag
    public string? GitBranch { get; init; }          // branch it was cut from
    public string ConfigurationHash { get; init; } = "";
    public string ProviderLockHash { get; init; } = "";
    public string? RequiredTerraformVersion { get; init; }

    public string? Notes { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string CreatedBy { get; init; } = "";
}
```

Versions can be created explicitly ("cut a version" → optionally push a Git tag) or inferred from tags/commits already in the repo. They are **not tied to a stage order** — deploying is a free mapping of `ProjectVersion → ProjectEnvironment`, so the Dev→UAT→Live sequence is a *recommended promotion path*, never a constraint. Custom environments participate identically.

## Version × environment matrix

The Pipelines tab offers two complementary views:

1. **Release-pipeline board** (below) — stage-oriented, good for promotion flows.
2. **Version matrix** — a grid of **versions (rows) × environments (columns)**, each cell showing whether that version is *currently deployed*, *previously deployed*, or *available to deploy* there. This makes the "v1 on Live / v1.5 on UAT / v2 on Dev" picture immediate, and lets a user select a version and **deploy it to one environment, several, or all** in one action (each target still runs its own governed plan/apply and confirmation gates).

```text
              Dev         UAT         Live        DR (custom)
  v2.0-dev    [current]   [deploy]    [deploy]    [deploy]
  v1.5        [previous]  [current]   [deploy]    [deploy]
  v1.0        [previous]  [previous]  [current]   [current]
```

Legend: `current` = live now · `previous` = was deployed before · `deploy` = available to deploy here.

An environment's **current version** = its latest `Succeeded` deployment ([Deployment](#version--environment-tracking) below). Nothing forces two environments to agree; they drift independently by design.

## Deployment board (release pipeline view)

A horizontal pipeline of environment **stages** in order (e.g. Dev → UAT → Live), each stage a card showing:

- Environment name + production indicator (text + icon + colour — [13-ui-design.md](13-ui-design.md)).
- **Currently deployed version**: Git commit SHA + short message, config hash, provider-lock hash.
- **Last successful deployment**: time, who, plan summary (adds/changes/destroys/replaces).
- **State pointer**: backend + workspace + serial/lineage of the state the last apply produced.
- **Drift badge**: result of the latest refresh-only plan ([06-plan-apply-safety.md](06-plan-apply-safety.md)).
- **Health**: last command status; whether an operation is currently running (env lock — [05-terraform-engine.md](05-terraform-engine.md)).
- **Promote** action → produces a plan for the target environment and opens the standard review/apply gates.

Between stages, arrows indicate promotion flow and whether the downstream environment is **behind** the upstream one (e.g. "UAT is 3 commits behind Dev").

## Version → environment tracking

Fenrix already records, per plan and apply, the Git commit, branch, working-tree status, config hashes, provider-lock hash, Terraform version, and plan/apply counts ([06-plan-apply-safety.md](06-plan-apply-safety.md), [12-database-design.md](12-database-design.md)). The Pipelines area is largely a **view over existing data** plus a new `Deployments` concept:

```csharp
public sealed class Deployment
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public Guid ProjectVersionId { get; init; }    // which project version this deployed
    public Guid PlanId { get; init; }              // the exact saved plan applied

    public string VersionLabel { get; init; } = ""; // denormalised for the board, e.g. "1.5"
    public string GitCommit { get; init; } = "";   // version deployed
    public string GitBranch { get; init; } = "";
    public string ConfigurationHash { get; init; } = "";
    public string ProviderLockHash { get; init; } = "";
    public string TerraformVersion { get; init; } = "";

    public string? StateBackend { get; init; }      // backend id / workspace
    public long? StateSerial { get; init; }          // state serial after apply
    public string? StateLineage { get; init; }

    public DeploymentStatus Status { get; init; }    // Queued, Planning, AwaitingApproval,
                                                     // Applying, Succeeded, Failed, RolledBack
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string InitiatedBy { get; init; } = "";
    public int AddCount, ChangeCount, DestroyCount, ReplaceCount;
}
```

`Deployment` never stores sensitive values — only summaries, hashes, and references ([11-secrets.md](11-secrets.md)). "Current version of an environment" = the `ProjectVersion` of its latest `Succeeded` deployment. State version is read from the backend after apply (serial + lineage), giving the exact state that is live. Because each environment has its own deployment timeline, environments hold **different versions simultaneously** with no coupling.

### Deploy one version to many environments

Selecting a version and choosing multiple targets (or "all environments") **fans out** into one governed deployment per environment — each with its own plan, safety gates, production typed-confirmation, and environment lock ([05-terraform-engine.md](05-terraform-engine.md)). A fan-out is not a transaction: environments succeed or fail independently, and the board reflects each result separately. This supports both "hotfix v1.0.1 to Live only" and "roll v2.0 out to Dev, UAT, and Live together."

## Pipeline definition

A **pipeline** is a per-project ordered list of environment stages plus per-stage rules:

```csharp
public sealed class DeploymentPipeline
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string Name { get; set; } = "";
    public List<PipelineStage> Stages { get; set; } = [];   // ordered
}

public sealed class PipelineStage
{
    public Guid EnvironmentId { get; set; }
    public bool RequireApproval { get; set; }               // gate before apply
    public bool RequirePreviousStageSuccess { get; set; }   // must promote in order
    public bool RequireCleanWorkingTree { get; set; }
    public bool RequireTypedConfirmationForProduction { get; set; } = true;
    public string? RequiredBranch { get; set; }             // e.g. only deploy 'main' to Live
    public List<string> Approvers { get; set; } = [];       // enterprise: role-gated
}
```

Defaults mirror the Dev → UAT → Live convention with production gates on the last stage. Pipelines are optional; a project without one still deploys per-environment from the Terraform page.

## One-click deploy — what actually happens

"Select environment → Deploy" runs the **standard governed pipeline** with as few clicks as the safety rules allow:

```text
Click Deploy on a stage
  → resolve environment working dir, var-file, backend, cloud connection
  → terraform init (if needed) / validate
  → terraform plan -out=<plan>  →  show -json  →  review card
  → evaluate stage rules (branch, clean tree, approval, production typed-confirm)
  → [approval gate if required]
  → acquire environment lock
  → terraform apply -input=false <plan>   (exact saved plan)
  → record Deployment (version, state serial/lineage, counts)
  → release lock  →  update board
```

For non-production stages with `RequireApproval = false` and a clean tree, this is genuinely one click through to apply. For production it is one click to *start*, then the typed-name confirmation ([06-plan-apply-safety.md](06-plan-apply-safety.md)). Nothing here weakens [ADR-0003](adr/0003-saved-plan-only-apply.md).

## Promotion & rollback

- **Promote**: take the exact commit/config that succeeded on an upstream stage and plan it against the downstream environment — so "what you tested is what you ship."
- **Rollback**: re-deploy the previous succeeded `Deployment`'s commit (Fenrix checks out that version and runs the governed plan/apply). Because Git and state versions are recorded, rollback targets are explicit. Rollback is a full plan/apply, never a silent state edit.

## Relationship to external CI/CD

Fenrix does **not** replace GitHub Actions / Azure Pipelines / GitLab CI. Two complementary modes:

1. **Fenrix-driven deployments** — the desktop (or Fenrix Agent) runs the governed plan/apply and records `Deployment`s. This is the release-pipeline UI above.
2. **External-pipeline visibility** — via the provider adapters ([09-provider-integrations.md](09-provider-integrations.md)), show upstream pipeline/Actions status on the board so users see CI results next to Fenrix deployments.

Enterprises can require that Live deployments only happen through the Fenrix Agent with approvals (Phase 11), turning the desktop into a controlled release console.

## Visual authoring → deploy (the "build every part" idea)

The request to "build every single part of the Terraform file — modules, everything — then deploy with one click" spans two features already in the plan, now connected end-to-end:

1. **Visual resource/module builder** ([07-visual-builder.md](07-visual-builder.md)) authors resources, data sources, variables, outputs, backends, and module blocks from provider schemas, previews HCL, and writes real `.tf` files. Advanced HCL still round-trips through the Monaco editor.
2. **Pipelines/Deployments** (this doc) then lets the user pick an environment and deploy the saved files with one governed click.

Together: **author visually → save real files (source of truth) → commit (version control) → select environment → deploy (saved-plan apply) → see the version live on the board.** The visual builder is Phase 10; a basic module/variable/output authoring surface can land earlier if prioritised.

## Delivery placement

- **MVP-adjacent, lightweight**: a **read-only Deployments board** built purely from existing plan/apply history — high value, low cost — can appear as soon as Phase 4 (saved plans) and Phase 5 (Git) exist.
- **Full Pipelines (Phase 9.5 / new Phase)**: pipeline definitions, stage gates, promote/rollback, approvals-lite. Slots after State & inspection (Phase 9) and before/alongside the visual builder (Phase 10).
- **Enterprise pipelines (Phase 11)**: central agent execution, role-based approvals, org-controlled Terraform versions, org-wide pipeline templates.

See [ROADMAP.md](ROADMAP.md) for the phase entry and [PROGRESS.md](PROGRESS.md) for the checklist.
