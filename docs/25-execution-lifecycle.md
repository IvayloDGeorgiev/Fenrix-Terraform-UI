# 25 · Execution Lifecycle & Provider Execution

The Terraform architecture is one loop:

```text
  Config (desired) ─┐
                    ├─►  CORE  ─►  Plan  ─►  Execute the plan with Providers  ─►  real systems
  State (current)  ─┘                         (AWS P. · K8s P. · GitHub P. · MySQL P. · …)
```

The **Core** compares config against state and computes a plan; **apply executes that plan through providers** — the plugins that talk to real systems. Providers are diverse: cloud (AWS, Azure, GCP), platform (Kubernetes), service/SaaS (GitHub, Fastly), and data stores (MySQL, PostgreSQL). A single project commonly spans several at once. This doc describes the execution steps Fenrix runs and how it makes each one visible.

> Fenrix does not execute providers itself — Terraform's Core does. Fenrix orchestrates the CLI, streams the structured execution events, and renders them as clear, per-step, per-provider progress. See [05-terraform-engine.md](05-terraform-engine.md) (engine) and [06-plan-apply-safety.md](06-plan-apply-safety.md) (safety).

## The execution steps

### 1. Resolve context
Select project + environment → resolve working directory, Terraform version (enforced), var-file, backend config, and the cloud/connection scope ([03](03-domain-model.md), [10](10-cloud-integrations.md)). Command preview shows exactly what will run ([23](23-command-transparency.md)).

### 2. Init — load providers & backend
`terraform init` downloads/loads the **required providers** and configures the backend. Fenrix surfaces which providers/versions were loaded and any lock-file changes (`.terraform.lock.hcl`, hashed for plan integrity — [06](06-plan-apply-safety.md)). This is where the "AWS P. / K8s P. / GitHub P. / MySQL P." set for the project becomes concrete.

### 3. Plan — Core computes the diff
`terraform plan -input=false -out=<plan> -var-file=<env.tfvars>` → `terraform show -json <plan>`. The Core reads **config vs state** and emits the diff: create / update / destroy / replace, grouped and filterable, with sensitive values redacted. Reviewed in the three-pane plan screen ([06](06-plan-apply-safety.md)).

### 4. Safety gates
Verify plan hash/existence, environment, cloud account; warn on branch/uncommitted/deletions/replacements/production; require typed confirmation for production ([06](06-plan-apply-safety.md), [ADR-0003](adr/0003-saved-plan-only-apply.md)). Acquire the per-environment operation lock ([05](05-terraform-engine.md)).

### 5. Apply — execute the plan with providers
`terraform apply -input=false <saved-plan>`. The Core walks the dependency graph and calls **each provider** to make the change it owns. Terraform streams JSON apply events; Fenrix renders them live:

- Per-resource **status transitions**: `Creating… → Created (2.4s)`, `Modifying…`, `Destroying…`, `Replacing…`.
- Which **provider** owns each resource (AWS, Kubernetes, GitHub, MySQL, …), so users see the plan executing across providers in real time.
- **Dependency ordering** — resources that unblock others; parallelism up to the configured limit.
- **Live output terminal** for the raw stream alongside the structured view ([13](13-ui-design.md)).

### 6. Record & reconcile
On completion: read the resulting **state serial + lineage** from the backend, record a redacted execution history entry and (for a governed deploy) a `Deployment` tying this exact version to the environment ([20](20-pipelines-deployments.md)), release the lock, and refresh drift status. Failures are classified with "whether infrastructure may have changed" made explicit ([16](16-error-handling.md)).

## Worked example — a multi-provider apply

A project whose config touches four providers. After review, the user clicks **Deploy → UAT**. Fenrix runs:

```text
Context   env=UAT  dir=environments/uat  TF=1.15.0  vars=uat.tfvars  cloud=aws:acct-123/eu-west-1
Preview   terraform apply -input=false ".../uat.tfplan"          [Copy]

Executing plan (12 changes: +7  ~3  -2) ──────────────────────────────────────
  [AWS P.]     aws_vpc.main                 Creating…      → Created    (3.1s)
  [AWS P.]     aws_subnet.app[0..2]         Creating…      → Created    (2.0s)
  [AWS P.]     aws_db_instance.core         Creating…      → Created   (41.7s)
  [K8s P.]     kubernetes_namespace.app     Creating…      → Created    (0.6s)
  [K8s P.]     kubernetes_deployment.api    Creating…      → Created    (4.2s)   depends_on: aws_db_instance.core
  [MySQL P.]   mysql_database.appdb         Creating…      → Created    (0.9s)   depends_on: aws_db_instance.core
  [MySQL P.]   mysql_user.service           Modifying…     → Modified   (0.4s)
  [GitHub P.]  github_repository.infra      Modifying…     → Modified   (1.1s)
  [GitHub P.]  github_branch_protection.main  Replacing…   → Replaced   (1.3s)
──────────────────────────────────────────────────────────────────────────────
Applied  ✔ 7 added · 3 changed · 2 destroyed        state serial 48 → 49
Recorded Deployment  v1.5 → UAT  (commit a1b9f2c, lineage 7c3…, by Ivo)
```

The user sees the **plan executing across all four providers**, in dependency order, with per-resource timing and the exact command that produced it — then the environment's version updates on the deployment board.

## Read-only inspection (no execution)

Not every step changes infrastructure. Fenrix exposes the read-only side of the loop too: `state list` / `state show` to inspect current state, `output` for outputs, `graph` for the dependency graph, and refresh-only planning for drift — all without executing providers against real systems ([05](05-terraform-engine.md), Phase 9).

## Concurrency & safety recap

Only **one state-changing execution per environment** (lock); read-only inspection may run concurrently. Apply always runs the **exact reviewed saved plan** — the execution never recomputes a different plan than the one approved ([ADR-0003](adr/0003-saved-plan-only-apply.md)).

## Delivery placement

Init/format/validate and streaming land in **Phase 3**; the full plan→apply execution view with per-provider, per-resource progress lands in **Phase 4**; the deployment recording in **Phase 4 / 9.5**. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
