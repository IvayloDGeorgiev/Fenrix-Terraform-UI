# 27 · Help & In-App Guidance

Fenrix exposes a lot of power (Terraform, Git, multi-cloud, pipelines). A dedicated **Help** area plus **contextual guidance** makes sure a user can understand *how everything works* without leaving the app. Help is a first-class navigation destination, not a buried link.

## Help tab (top-level)

A **Help** entry in the global left navigation ([13-ui-design.md](13-ui-design.md)) opens a Help home with:

- **Getting started** — create your first project, import an existing one, run your first plan/apply, connect a cloud account and a Git host.
- **Feature guides** — one page per area (Projects, Environments, Editor, Terraform commands, Plans & apply safety, Git, Connections, Pipelines & deployments, State, Visual builder, Settings), each explaining what the screen does, the safe workflow, and the underlying command.
- **Concepts** — the config-vs-state model ([22](22-terraform-files-model.md)), the execution lifecycle ([25](25-execution-lifecycle.md)), why saved-plan-only apply ([06](06-plan-apply-safety.md)), the connections model ([26](26-connections.md)).
- **Reference** — every Terraform/Git command Fenrix can run, with the exact CLI it maps to.
- **Troubleshooting & FAQ** — common errors ([16](16-error-handling.md)) with recovery steps.
- **Keyboard shortcuts** — full list, also reachable via a shortcut overlay.
- **About & diagnostics** — versions of Fenrix, Terraform, Git, and CLIs; links to export diagnostics ([15](15-logging-auditing.md)).

Help content ships **with the app** (works offline) and is **searchable** from a single box. The same content is the source for the contextual help below, so there is one source of truth.

## Contextual, in-context guidance

Help meets the user where they are, not only in the Help tab:

- **"?" affordances** on every screen and complex field open the relevant Help page inline (a side panel), without losing the user's place.
- **Command explanations.** Everywhere Fenrix shows the exact command it will run ([23-command-transparency.md](23-command-transparency.md)), a "What does this do?" toggle explains the command and each flag in plain language — turning the app into a way to *learn* Terraform/Git, not just drive them.
- **Inline tips & empty states.** Empty screens (no projects, no connections, no plans yet) explain what the screen is for and the next action, with a shortcut to do it.
- **Guardrail explanations.** Safety gates (production confirmation, stale-plan block, missing connection) explain *why* they're blocking and how to proceed safely ([06](06-plan-apply-safety.md), [26](26-connections.md), [16](16-error-handling.md)).
- **Tooltips** on icons and status badges (e.g. production marker, drift badge, ahead/behind) with text — never relying on colour alone ([24](24-visual-design-language.md)).

## Guided tours & onboarding

- A **first-run tour** highlights the shell (left nav, top bar, command palette, theme toggle) in a few dismissible steps.
- **Task tours** walk through multi-step flows the first time: "create a project", "run a plan and apply", "connect a cloud account", "make your first commit". Tours are replayable from Help and never forced.
- Progress is remembered so tours don't repeat; everything is skippable for power users.

## Command palette (fast navigation + discovery)

A **command palette** (top bar, keyboard-invoked — [13](13-ui-design.md)) is both navigation and help: type to jump to any screen, project, or environment, or to run an action; results show the action's shortcut and a one-line description, so users discover features by searching for what they want to do.

## Themes & accessibility (part of a smooth experience)

- **Dark and Light** themes (Dark is the default) are switchable instantly from the top bar and in Settings → Appearance, built on the design-token system so the switch is seamless ([24-visual-design-language.md](24-visual-design-language.md)).
- **High-contrast** theme and full **keyboard navigation** make the app usable for everyone; Help documents the shortcuts and accessibility options.
- Help itself respects reduced-motion and contrast settings.

## Delivery placement

The Help **framework** (Help tab shell, searchable content, contextual "?" panel, command-explanation toggle, theme toggle) is lightweight and lands early — with the navigation shell and theme system in **Phase 1**. Per-feature Help pages and task tours are written **alongside each feature** as it ships, so guidance never lags the product, and are completed in the **Phase 12** documentation pass. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
