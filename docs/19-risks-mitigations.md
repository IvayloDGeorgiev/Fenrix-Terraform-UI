# 19 · Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| **Terraform output changes** between versions | Parse versioned JSON; ignore unknown minor-version fields; reject unsupported major formats; maintain output fixtures from multiple versions; keep raw terminal fallback. |
| **Destructive operations** damage infrastructure | Risk classifications; saved-plan-only apply; typed environment confirmation for production; plan hashes; production markers; audit history; disable dangerous actions while another command runs. |
| **Secret leakage** into logs/db/manifests | Secret references only; Windows secure storage; log redaction; no raw plan JSON persistence; no credentials in manifests; clear-clipboard options. |
| **External filesystem changes** cause conflicts or lost work | Watcher plus reconciliation; content hashes; atomic writes; conflict prompts; database used only as an index. |
| **Git-provider API differences** | Keep core Git operations provider-independent; use small provider adapters; expose provider capability flags; fall back to generic Git. |
| **Full Git-client scope** is huge | Deliver Git in layers; make clone/status/commit/branch/pull/push reliable first; add interactive rebase, reflog, worktrees, and advanced conflict tools later. |
| **SQL Server expectations** (mistaken for remote execution) | Clearly separate shared metadata from remote execution; introduce a Fenrix Agent before offering central deployments. |

Each risk maps to concrete controls documented elsewhere: destructive-op safety in [06](06-plan-apply-safety.md) and [ADR-0003](adr/0003-saved-plan-only-apply.md); output parsing in [05](05-terraform-engine.md); secrets in [11](11-secrets.md); filesystem in [04](04-filesystem-sync.md) and [ADR-0002](adr/0002-files-as-source-of-truth.md); providers in [09](09-provider-integrations.md); Git layering in [08](08-git-engine.md) and [ROADMAP.md](ROADMAP.md); enterprise scope in [12](12-database-design.md).

## Final architectural stance

Fenrix is a **desktop orchestration and visualisation layer** around established infrastructure tools:

```text
.NET MAUI Blazor Hybrid UI
  → Application use cases and safety policies
  → Terraform, Git and cloud adapter interfaces
  → Official command-line tools and provider APIs
  → Cloud platforms, Git providers and local files
```

The three rules that keep the risks contained: **Terraform and Git remain the engines; files on disk are the source of truth; no change is applied unless the exact reviewed plan passes the safety checks.**
