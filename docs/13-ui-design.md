# 13 · UI Design

Blazor Hybrid UI hosted in the MAUI Windows shell. Modern desktop navigation comparable to Fork / Visual Studio, with a Monaco-based code editor embedded in the Blazor WebView.

> **This doc covers structure & navigation.** The *look and feel* — the modern, animated, futuristic aesthetic, design tokens, and motion system — is specified in [24-visual-design-language.md](24-visual-design-language.md). The two share the accessibility rules below.

## Navigation

**Left navigation (global):** Dashboard · Projects · Source Control · **Connections** · Activity · Templates · **Help** · Settings. (The **Connections** hub is the global library of cloud + repository connections — see [26-connections.md](26-connections.md); the **Help** area explains how everything works — see [27-help-and-guidance.md](27-help-and-guidance.md).)

**Bottom navigation:** Notifications · Background operations · Account · About.

**Project navigation (when a project is open):** Overview · Files · Environments · Terraform · Plans · State · Outputs · Graph · Source Control · Activity · Project Settings.

**Global top bar:** current project · current environment · current Git branch · working-tree status · selected cloud account · Terraform version · command palette · Run Plan button · notifications.

## Environment visibility & production safety

The currently selected environment must **always be visible**. Production environments carry a persistent production indicator that does **not rely on colour alone** — include text and an icon for accessibility.

## Code editor (Monaco)

Embedded in the Blazor WebView. Required features: HCL syntax highlighting, JSON highlighting, line numbers, search & replace, multiple tabs, minimap option, bracket matching, error markers, diff editor, read-only plan/state views, unsaved-file indicators, and keyboard shortcuts.

## Command preview (everywhere)

Every action that shells out renders a read-only **"command that will run"** label that updates live as the user changes options, with a Copy button and redacted secrets. This applies to Terraform, Git, and cloud-CLI actions alike. It is a shared UI component so all command screens inherit it. Full spec in [23-command-transparency.md](23-command-transparency.md).

## Terraform page

Sections: Quick actions · Init · Validate · Format · Plan · Apply · Destroy · Test · Import · Advanced commands · Live output terminal. Each action shows its command preview before running.

## Plan review

Three-pane design — **resource list | resource details | before/after comparison** — with summary cards for Add / Change / Destroy / Replace and filters (see [06-plan-apply-safety.md](06-plan-apply-safety.md)).

## Git page

Modern Git-client layout: **branches & remotes | commit graph | commit/file details**. Working-changes screen: **changed files | diff | commit message & actions**.

## Settings page

Sections: General · Appearance · Workspace paths · Terraform versions · Git · Cloud accounts · Version-control providers · Database · Security · Logging · Updates · Advanced · Diagnostics (see [14-settings.md](14-settings.md)).

## Appearance

Light / Dark / System modes · compact & comfortable density · adjustable editor and terminal fonts · high-contrast compatibility · resizable panels · saved layouts · full keyboard navigation.

## Accessibility principles

- Never encode state in colour alone (text + icon + colour).
- Full keyboard navigation and focus order.
- High-contrast theme support.
- Respect system font-size and reduced-motion preferences where feasible.
