# Fenrix IaC Studio — User Guide

Fenrix is a Windows desktop studio for working with Terraform safely. It drives the official `terraform` and
`git` CLIs for you, shows the exact command before it runs, and keeps your files as the source of truth. This
guide takes you from install to your first reviewed apply, then through Git, cloud connections, and the rest.

## Before you start

Fenrix runs the tools you already have; it does not bundle them. Make sure these are on your `PATH` (or set their
paths in **Settings**):

- **Terraform** — required for every Terraform screen. Check with `terraform version`.
- **Git** — required for source control. Check with `git --version`.

On first launch Fenrix creates its workspace at `C:\FenrixSource\FenrixIaCStudio\` (or, if that isn't writable,
under your `%LOCALAPPDATA%`). Your projects live wherever you choose — Fenrix never moves your files.

## 1 · Create or import a project

Open **Projects** and either create a new project or import an existing one.

A **new project** is scaffolded with a sensible layout and its Dev, UAT, and Live environments, and a Git repo is
initialised automatically. An **imported project** is read in place — Fenrix does not restructure your existing
Terraform. Point it at a folder and it maps what it finds. You can add your own environments at any time; each
environment is just a named working context (its own workspace, cloud binding, and locks).

## 2 · Edit files

Open a project to see its file tree and the built-in editor. The editor understands HCL: syntax highlighting,
bracket matching, an outline, snippets, and schema-aware reference helpers (`var.`, `local.`, `module.`,
`data.`, resource attributes). **Beautify** runs `terraform fmt` over the buffer; **Validate** surfaces
`terraform validate` diagnostics inline. Changes you make in Windows Explorer or another editor are detected and
reconciled automatically, and every save is versioned so you can recover an earlier copy.

## 3 · Run Terraform

From a project, use the **Terraform** screen for `init`, `format`, `validate`, and `version`, each as a typed
form. Every action shows a live **command preview** — the exact `terraform …` invocation, with any sensitive
values redacted — before you run it, and streams output as it happens. A redacted history of every run is kept.

## 4 · Plan & apply safely

The **Plan & apply** screen is where changes reach real infrastructure, and it is deliberately careful:

1. Generate a **saved plan** (`plan -out`). Fenrix parses it and shows a graphical review: additions,
   modifications, deletions, and replacements, with before/after values and sensitive values redacted.
2. Apply the **exact plan you reviewed** — nothing else. If the config or provider lock changed after the plan
   was made, the plan is marked stale and blocked, so you can never apply something you didn't review.
3. Production environments require a typed confirmation. Destroy and drift (refresh-only) have their own guarded
   flows. One state-changing operation runs per environment at a time (an on-disk lock).

## 5 · Cloud connections

Create a connection once in **Connections** (Azure, AWS, or Google), then **bind it to an environment**. Fenrix
composes the credentials at run time and leans on the native tool stores (az cache, AWS profiles/SSO, gcloud
ADC); the only secret it ever holds is an Azure service-principal secret, kept in Windows Credential Manager and
resolved just-in-time. Each environment can point at a different cloud account. A state-changing run is blocked
until the environment is bound, so you never apply to the wrong account.

## 6 · Source control

The **Source control** screen gives you staging, commit (with amend/sign-off), fetch/pull/push, branches,
history, a read-only diff viewer, stashes, merges with conflict resolution, tags, worktrees, and more —
each with the same command preview and redacted history. Remote operations run non-interactively against your
existing Git Credential Manager credentials. If a repository host is connected (**GitHub, GitLab, Azure DevOps,
Bitbucket**), the Provider panel adds repository browsing, pull/merge requests, pipeline status, and branch
policies.

## 7 · Keys, inspection, and deployments

- **Keys** manages per-project SSH/EC2 key pairs. Import an existing key or generate one; private keys are
  encrypted at rest with DPAPI, outside your project and never in Git. Export is off by default and audited.
- **Inspect** is a read-only view of state, outputs, a visual dependency graph, drift, and an import assistant.
- **Pipelines** shows a deployment board and a version × environment matrix, with governed one-click deploy
  (plan → gates → apply the exact saved plan), promote/rollback, and fan-out.

## 8 · Enterprise mode (optional)

Fenrix is single-user by default. Dropping an `enterprise.json` into the data root turns on governance: shared
metadata (SQLite or SQL Server), role-based access, a central audit trail, shared policy and templates, and
role-gated approvals. Nothing changes for a single user who never enables it — and when enabled, governance only
ever adds gates, never removes them. See [29-enterprise.md](29-enterprise.md).

## 9 · Backup & recovery

Fenrix snapshots its metadata database on every launch (kept under `Backups/`), including just before it applies
any database migration on an upgrade — so a bad upgrade is recoverable. If the app is closed unexpectedly, the
next launch notices and surfaces the latest backup. Your Terraform files are always the source of truth; the
database is an index and a recovery cache.

## Tips

- Everything sensitive is redacted in previews, history, and logs. Keep project repositories **private** anyway —
  plan and state files contain plaintext secrets by design.
- Switch between **Dark** and **Light** themes in Settings. Reduced-motion is honoured automatically.
- Stuck on a screen? The command preview always tells you exactly what Fenrix is about to run.
