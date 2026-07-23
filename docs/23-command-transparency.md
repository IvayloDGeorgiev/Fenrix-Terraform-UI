# 23 · Command Transparency (Show the Actual Command)

Fenrix replaces typing commands with UI, but it **never hides what it runs.** Every functionality that maps to a CLI invocation shows a **live label of the exact command** that will execute on the backend — Terraform, Git, and the cloud CLIs alike. Users see, and can copy, precisely what Fenrix will run before they run it, and what it did run afterwards.

This is a core product promise: *a graphical interface over the real tools, with nothing happening behind the user's back.*

## The rule

**If a UI action shells out, it must display the command it will run.** Wherever the user configures an operation (a form, a toggle, a screen, the command builder), Fenrix renders a read-only, always-up-to-date preview that reflects the current options. As the user changes inputs (var-file, flags, target, workspace, etc.), the preview updates instantly.

```text
┌──────────────────────────────────────────── Plan (Dev) ─────────┐
│  ☑ -input=false     Var file: dev.tfvars    Parallelism: 10      │
│  ☐ -refresh-only    ☐ -target=…             Lock timeout: 0s     │
│                                                                  │
│  Command that will run:                                    [Copy]│
│  terraform plan -input=false -out=".../dev.tfplan" \             │
│      -var-file="dev.tfvars" -parallelism=10                      │
│                                                                  │
│  Working dir: environments/dev   ·   TF v1.15.0   ·   Cloud: … │
│                                            [ Cancel ]  [ Run ▸ ] │
└──────────────────────────────────────────────────────────────────┘
```

## What the label shows

- The **executable** (which `terraform.exe` / `git.exe` / `az` etc., resolved path on hover).
- The **exact arguments**, in order, quoted as they will be passed — built from `ArgumentList`, not a shell string ([05-terraform-engine.md](05-terraform-engine.md)).
- The **working directory** the command runs in.
- The **environment context** that matters: selected Terraform version, cloud connection/subscription, workspace — shown as chips, not as raw secret values.
- **Secrets are never rendered.** Sensitive environment variables and credential values are shown as `••••` / named references only ([11-secrets.md](11-secrets.md)); the command preview and any copied text are redacted.

## Coverage — every functionality

Because Fenrix aims to expose **every Terraform capability** (see [Provider & core model](#terraform-is-core--providers) below) through one of three surfaces, command transparency applies to all three:

1. **Typed command screens** (init, validate, plan, apply, destroy, import, output, show, graph, state, workspace, …) — each screen has a command-preview panel.
2. **Dynamic command builder** — as the user assembles a command from discovered `-help` metadata, the preview is literally the command being built.
3. **Embedded terminal** — the user already sees the raw command; Fenrix echoes the command it injects for any UI-initiated interactive run.

The same applies to **Git** ([08-git-engine.md](08-git-engine.md)) — commit, push, rebase, reset, etc. show the `git …` that will run — and to **cloud CLI** discovery actions (`az login`, `aws sso login`, `gcloud auth …`).

## Before and after

- **Before running:** the preview shows what *will* run; the Run button executes exactly that. No divergence between preview and execution — they are generated from the same argument list.
- **After running:** the execution history and logs record the **redacted** command that *did* run, with exit code and timing ([15-logging-auditing.md](15-logging-auditing.md), [16-error-handling.md](16-error-handling.md)). "Copy command" and "Copy diagnostics" are available.

## Why this matters

- **Trust & learning.** Users understand and learn Terraform/Git instead of being locked into a black box; they can reproduce a command in a plain terminal or a CI pipeline.
- **Reviewability.** Destructive operations are easier to sanity-check when the exact command (and its `-target`, `-replace`, `destroy`, `reset --hard`, etc.) is visible next to the safety gates ([06-plan-apply-safety.md](06-plan-apply-safety.md)).
- **Auditability.** The command shown, run, and logged are the same thing (redacted), closing the gap between UI intent and CLI reality.

## Terraform is Core + Providers

The architecture slide underlines that Terraform has **two main components**: the **Core** (reads config + state, computes the plan) and **Providers** (the plugins that talk to real systems). Providers are not limited to the big clouds:

- **IaaS** — AWS, Azure, Google Cloud, and other infrastructure providers.
- **PaaS** — Kubernetes and similar platform providers.
- **SaaS** — Fastly, and any service with a Terraform provider.

Fenrix therefore treats **any Terraform provider as first-class**. It does not hard-code a fixed provider list: provider knowledge comes from the installed binary via `terraform providers schema -json` ([07-visual-builder.md](07-visual-builder.md)), so the visual builder, resource browser, and command previews work for **whatever providers a project uses** — cloud, Kubernetes, SaaS, or niche. The dedicated cloud integrations ([10-cloud-integrations.md](10-cloud-integrations.md)) add *account/credential convenience* for AWS/Azure/GCP on top of this, but they are an enhancement, not the boundary of what Fenrix supports.

## Delivery placement

The command-preview component is **foundational UI plumbing**: build it with the process runner and typed screens in **Phase 3** so every subsequent command screen inherits it for free, and extend it to Git in **Phase 5**. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
