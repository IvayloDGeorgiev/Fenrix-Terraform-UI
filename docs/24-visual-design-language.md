# 24 · Visual Design Language

Fenrix should feel like a **modern, animated, futuristic** tool — not a generic line-of-business app. This doc defines the aesthetic and the motion system so the look is built consistently from Phase 1, and stays fast and accessible.

> Companion to [13-ui-design.md](13-ui-design.md), which covers *structure and navigation*. This doc covers *look and feel*. Where they overlap (themes, accessibility), 13 sets the rules and 24 sets the style.

## Design principles

1. **Futuristic but purposeful.** Depth, glow, and motion serve clarity and feedback — never decoration for its own sake. This is a tool engineers use all day; calm confidence beats flashy noise.
2. **Dark-first.** The primary experience is a deep, near-black canvas with luminous accents; light and high-contrast themes are first-class, not afterthoughts.
3. **Motion with meaning.** Every animation communicates something — state change, causality, progress, or spatial relationship. Motion is quick, smooth, and interruptible.
4. **Depth over borders.** Prefer elevation, blur, and subtle gradients to heavy 1px borders and boxes.
5. **Consistent through tokens.** All of the above is expressed as design tokens (CSS custom properties) so themes and density swap cleanly and nothing is hard-coded.
6. **Never at the cost of speed or access.** 60fps, GPU-friendly animations, and full respect for reduced-motion and contrast needs.

## Signature look

- **Deep space canvas** with a very subtle animated gradient / aurora backdrop that drifts slowly (heavily throttled; static under reduced-motion).
- **Glass panels** — translucent surfaces with backdrop blur and a faint inner highlight, floating above the canvas with soft shadows.
- **Luminous accents** — a primary neon accent (electric indigo/cyan family) used for focus, active state, and key actions; a restrained secondary for success/warn/danger. Production/destructive states get their own unmistakable treatment (see [safety](#safety--status-color) below).
- **Hairline glow** on focus and active elements instead of hard outlines.
- **Crisp mono for command/CLI** surfaces, giving the command-preview labels ([23-command-transparency.md](23-command-transparency.md)) a terminal-grade, engineered feel.

## Design tokens

Everything is a CSS custom property on a theme root, so themes are a token swap. Illustrative set (final values tuned during Phase 1):

```css
:root[data-theme="dark"] {
  /* canvas & surfaces */
  --fx-bg:            #0a0b12;
  --fx-surface:       rgba(22, 24, 38, 0.72);   /* glass */
  --fx-surface-solid: #161826;
  --fx-elev-1:        0 1px 2px rgba(0,0,0,.4);
  --fx-elev-2:        0 8px 24px rgba(0,0,0,.45);
  --fx-blur:          14px;

  /* accents */
  --fx-accent:        #6d6cff;   /* electric indigo */
  --fx-accent-2:      #22d3ee;   /* cyan */
  --fx-glow:          0 0 0 1px rgba(109,108,255,.5), 0 0 18px rgba(109,108,255,.35);

  /* semantic */
  --fx-success:       #34d399;
  --fx-warn:          #fbbf24;
  --fx-danger:        #fb5c6b;
  --fx-prod:          #ff8a3d;   /* production marker (paired with icon + text) */

  /* text */
  --fx-text:          #e8eaf2;
  --fx-text-dim:      #9aa0b4;

  /* typography */
  --fx-font-ui:       "Inter", "Segoe UI Variable", system-ui, sans-serif;
  --fx-font-mono:     "JetBrains Mono", "Cascadia Code", ui-monospace, monospace;

  /* shape & rhythm */
  --fx-radius:        14px;
  --fx-radius-sm:     8px;
  --fx-space:         8px;       /* 8px spacing grid */

  /* motion */
  --fx-dur-fast:      120ms;
  --fx-dur-base:      200ms;
  --fx-dur-slow:      360ms;
  --fx-ease:          cubic-bezier(.2, .8, .2, 1);   /* smooth, slightly springy */
  --fx-ease-out:      cubic-bezier(0, 0, .2, 1);
}
```

Light and high-contrast themes redefine the same tokens ([13-ui-design.md](13-ui-design.md) → Appearance). Compact/comfortable density scales `--fx-space` and control heights.

## Typography

- **UI:** a modern variable sans (Inter / Segoe UI Variable). Tight, confident headings; generous line-height for body and logs.
- **Mono:** JetBrains Mono / Cascadia Code for command previews, file paths, plan output, diffs, and the terminal — reinforcing the engineered feel and matching Monaco.
- Use type scale steps (e.g. 12 / 13 / 14 / 16 / 20 / 28) rather than arbitrary sizes.

## Motion system

Motion is defined by a small vocabulary so it feels coherent, not random.

| Interaction | Motion | Duration |
|-------------|--------|----------|
| Hover / press feedback | subtle scale (1.0→1.02) + glow rise | `--fx-dur-fast` |
| Panel / dialog enter | fade + rise (8–12px) + blur-in | `--fx-dur-base` |
| Route / page change | shared-axis slide + fade | `--fx-dur-base` |
| List/row insert & reorder | staggered fade-slide | `--fx-dur-base`, 20–30ms stagger |
| Plan summary counts (add/change/destroy) | count-up + accent pulse | `--fx-dur-slow` |
| Long-running command | animated progress shimmer + streaming log auto-scroll | continuous, throttled |
| Success / failure | check draw-in / shake, with color + icon | `--fx-dur-base` |
| Deployment board version move | animated token travel between stages | `--fx-dur-slow` |

Rules: animate **transform** and **opacity** (GPU-friendly), avoid animating layout properties; keep everything interruptible; never block input on animation; cap concurrent large animations.

## Depth & effects

- **Glassmorphism** for panels/overlays: `backdrop-filter: blur(var(--fx-blur))` over the animated canvas, with a 1px inner top highlight.
- **Soft neon focus** rings (`--fx-glow`) instead of default browser outlines — but focus must remain clearly visible for keyboard users.
- **Gradient accents** on primary actions and active nav, kept subtle.
- **Micro-interactions**: buttons, toggles, tabs, and the command palette all have tactile hover/press/active states.

## Safety & status color

Futuristic styling never weakens the safety signals from [13-ui-design.md](13-ui-design.md) and [06-plan-apply-safety.md](06-plan-apply-safety.md):

- **Production** environments use `--fx-prod` **plus** a lock/star icon **plus** the word "Production" — never colour alone.
- **Destructive** actions (destroy, reset --hard, force push) use `--fx-danger`, a warning icon, and a distinct heavier button treatment; glow does not soften them into looking safe.
- Plan deltas: additions `--fx-success`, changes `--fx-warn`, destroys/replacements `--fx-danger`, each with a shape/icon as well as colour.

## Accessibility (non-negotiable)

- **Respect `prefers-reduced-motion`:** disable the animated backdrop, count-ups, travel animations, and shimmer; replace with instant state changes or a single short fade. Provide a Settings toggle too (Appearance).
- **Contrast:** all text and essential UI meet WCAG AA against their surface; the high-contrast theme raises this further and drops translucency.
- **Never colour alone:** pair every status colour with text and/or icon.
- **Focus visible:** the neon focus style must be obvious under keyboard navigation.
- **Backdrop blur is decorative:** content legibility never depends on it; solid-surface tokens exist for when blur is disabled or unsupported.

## Implementation approach (Blazor Hybrid)

- The UI runs in a WebView, so the look is **HTML/CSS** driven by the token system above — a natural fit for gradients, blur, and CSS/Web-Animations.
- Use **CSS custom properties + a small utility layer** (hand-rolled or a utility CSS framework) so themes/density are pure token swaps; avoid deep per-component overrides.
- Prefer **CSS transitions / Web Animations API** and lightweight, dependency-free effects over heavy JS animation libraries, to protect WebView performance and startup time.
- Isolate effects behind classes/tokens so reduced-motion and high-contrast can switch them off wholesale.
- Test animation cost on the actual Windows WebView; budget for 60fps and fast cold start ([17-testing-strategy.md](17-testing-strategy.md) → performance; Phase 12 performance testing).

## Delivery placement

The **theme + token system, base components, and motion vocabulary** are built with the theme system in **Phase 1** so every later screen inherits the look for free. Signature flourishes (deployment-board token travel, plan count-ups) land with their features. A **polish/accessibility/performance pass** is part of **Phase 12**. Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
