# 22 · Terraform Files & Authoring Model

Terraform's core takes **two inputs** and produces a plan:

```text
  TF-Config (desired state) ─┐
                             ├─►  CORE  ─►  Plan: what to create / update / destroy
  State     (current state) ─┘
```

- **Config** — the `.tf`/`.tfvars`/`.hcl` files the user authors: *what infrastructure should exist.*
- **State** — Terraform's record of *what currently exists*.
- **Plan** — the diff between them.

Fenrix's job is to make **every file on the config side easy to create, update, and manage**, and to make the **state side safe to inspect and, rarely, operate on** — while Terraform's core does the actual diffing. This doc is the checklist that ensures no Terraform file type is left unhandled.

## Config-side files Fenrix handles

| File / pattern | Purpose | How Fenrix helps author it |
|----------------|---------|----------------------------|
| `main.tf` | Primary resources & module calls | Visual resource/module builder ([07](07-visual-builder.md)) + Monaco editor |
| `providers.tf` (or `terraform`/`provider` blocks) | Provider requirements & config | Provider picker from installed schemas; version constraints surfaced |
| `versions.tf` | `required_version`, `required_providers` | Detected & enforced against selected Terraform version ([05](05-terraform-engine.md)) |
| `variables.tf` | Input variable declarations | Form-based variable editor (name, type, default, description, sensitive, validation) |
| `outputs.tf` | Output declarations | Form-based output editor (value, description, sensitive) |
| `locals.tf` / `locals` blocks | Local values | Editor-first; simple literals via forms |
| `*.tfvars` / `*.tfvars.json` | Per-environment variable values | Per-environment values editor; mapped to environments ([03](03-domain-model.md)) |
| `backend.hcl` / `backend` block | Remote state backend config | Backend config form; referenced per environment |
| `terraform.tf` | `terraform {}` settings (backend, cloud, required_*) | Guided settings form |
| `*.tf` modules under `modules/` | Reusable modules | Module scaffolder: create module folder with `variables.tf`/`main.tf`/`outputs.tf`; wire module calls |
| `data` blocks | Data sources | Schema-driven data-source builder ([07](07-visual-builder.md)) |
| `.terraform.lock.hcl` | Provider dependency lock | Read + hashed for plan integrity ([06](06-plan-apply-safety.md)); managed by `init`, never hand-edited |
| `.tfignore` / `.gitignore` | Ignore rules | Suggested entries on project create/import ([03](03-domain-model.md)) |
| `README.md` | Docs | Created for new projects |

## State-side files Fenrix handles

| File / pattern | Purpose | How Fenrix helps |
|----------------|---------|------------------|
| `terraform.tfstate` (local) | Current state | Read-only state browser; never hand-edited (Phase 9, [ROADMAP](ROADMAP.md)) |
| `terraform.tfstate.backup` | Previous state | Surfaced for recovery awareness |
| Remote state (backend) | Shared state | Inspected via `terraform show`/`state list`; backend config per environment |
| `*.tfplan` (saved plan) | Reviewed plan artifact | Produced by `plan -out`, hashed, applied exactly ([06](06-plan-apply-safety.md)) |

State is **never edited as text** in Fenrix. State operations (`state list/show/mv/rm`, force-unlock, push) go through governed, warning-gated commands ([05](05-terraform-engine.md), Phase 9). This preserves the two-input model: users shape the *config*, Terraform owns the *state*.

## Create / update / manage UX

The goal — *easy to create or update and manage every file* — is delivered through three complementary surfaces, all writing **real files on disk** (source of truth, [ADR-0002](adr/0002-files-as-source-of-truth.md)):

1. **New-project scaffolding** ([03](03-domain-model.md)) generates the full config-side file set per environment (`main`, `providers`, `variables`, `outputs`, `*.tfvars`, `backend.hcl`) so a user starts complete, not from a blank folder.
2. **Visual builders** ([07](07-visual-builder.md)) author resources, data sources, modules, variables, and outputs from provider schemas with live HCL preview, then write the file. Advanced HCL round-trips through Monaco untouched.
3. **Monaco editor** ([13](13-ui-design.md)) is always available for anything the builders don't cover, with HCL highlighting, diff, error markers, and unsaved indicators.

Every create/update is atomic, watched, versioned for recovery ([21](21-file-history-recovery.md)), and reflected in Git status ([08](08-git-engine.md)). Once files are saved and committed, the user selects an environment and deploys via the governed one-click flow ([20](20-pipelines-deployments.md)).

## Coverage guarantee

Fenrix commits to handling **the complete config-side file set** (author/edit/manage) and **safe read/operate access to the state side**. If a project contains a file type not yet covered by a visual builder, it is still fully editable in Monaco and never rewritten or lost — Fenrix preserves unsupported HCL as raw source ([07](07-visual-builder.md)). No Terraform file falls outside the app's management.

## End-to-end flow

```text
Author (scaffold / visual builder / editor)   →  real .tf/.tfvars/.hcl on disk
  →  version history snapshot ([21])           →  commit ([08])
  →  select environment                         →  plan (config VS state → CORE → plan) ([06])
  →  review + safety gates                      →  apply exact saved plan
  →  deployment recorded, version live on board ([20])
```

This keeps Fenrix faithful to the core model in the diagram: users manage the **config** side comprehensively and easily; Terraform's core compares it against **state** to produce the plan; Fenrix makes the whole loop visual, safe, and one-click.
