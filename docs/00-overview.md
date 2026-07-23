# 00 · Overview

## Product vision

Fenrix IaC Studio makes Terraform easier to use **without attempting to replace Terraform itself**. It is a Windows desktop application that gives DevOps engineers a graphical interface over the real Terraform and Git command-line tools, plus project organisation, cloud-account selection, visual plan review, and strong safety checks around destructive operations.

Recreating Terraform's execution engine inside .NET would be difficult, insecure, and would inevitably behave differently from the official CLI. Fenrix therefore treats Terraform and Git as the engines and positions itself as the cockpit.

## Separation of concerns

**Terraform remains responsible for** parsing HCL, loading providers and modules, generating plans, maintaining and locking state, applying changes, and talking to provider APIs. (Terraform has two main components: the **Core**, which reads config + state and computes the plan, and **Providers**, the plugins that talk to real systems — not just IaaS clouds like AWS/Azure/GCP but also PaaS like Kubernetes and SaaS like Fastly. Fenrix treats **any Terraform provider as first-class**; provider knowledge comes from the installed binary's schema, so it is never limited to a hard-coded provider list. See [23-command-transparency.md](23-command-transparency.md).)

**Fenrix is responsible for** project and environment organisation, command construction and execution, visual plan review, safety checks, Git operations, cloud account selection, file editing, environment variables, credential *references*, logs and execution history, user confirmations, and modern desktop navigation.

Terraform exposes structured JSON for validation, plans, state and long-running commands (`plan`, `apply`, `refresh`, `test` can emit JSON event streams; saved plans convert via `terraform show -json`). These interfaces are the foundation of the graphical experience.

## Product principles

### 1. Files remain the source of truth

The `.tf`, `.tfvars`, `.hcl`, `.json`, state and Git files on the Windows filesystem are authoritative. SQLite must **not** become a second copy of the project. The database stores only registrations, environment mappings, settings, execution history, cached results, UI state, connection references, recent files, and plan summaries.

### 2. Use official command-line tools

Drive `terraform.exe`, `git.exe`, and optionally `az`, `aws`, `gcloud` for account discovery. Provider REST APIs are used **only** for host-specific features (repository discovery/creation, pull/merge requests, pipeline status, branch policies, identity, org/workspace/project discovery). Normal Git operations always go through Git itself.

### 3. Safe operations by default

Potentially destructive operations require extra confirmation: `apply`, `destroy`, state removal/movement/push, force-unlock, resource replacement, `git reset --hard`, `git clean`, force push, branch deletion, discarding uncommitted changes.

### 4. Existing projects are never forced into a new structure

New projects may use Fenrix's recommended layout. Existing projects keep their folders, workspaces, or environment directories and may live anywhere on the machine — inside or outside the Fenrix projects directory. Fenrix stores a **logical mapping** between an environment and its real working directory; it does not move or rewrite files.

## Non-goals (initially)

- Reimplementing Terraform's parser, planner, or state engine.
- A full graphical translation of every HCL expression (functions, dynamic blocks, loops, conditionals). The text editor remains authoritative for advanced configuration.
- Central multi-user remote execution. A later **Fenrix Agent** service is required before SQL Server metadata turns into centrally controlled deployments.

## Who it is for

DevOps and platform engineers on Windows who use Terraform daily and want a safer, more visual workflow with first-class Git and multi-cloud account handling — comparable in polish to modern Git clients like Fork or the Visual Studio Git experience.
