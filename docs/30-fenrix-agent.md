# 30 · Fenrix Agent (design only — future phase)

The Fenrix Agent is a **central execution service**: an organisation runs Terraform on a controlled,
credentialed host (with an approved binary, network egress, and state backend) and the desktop becomes a
**release console** that requests runs rather than executing them locally. This document captures the
**design and the code seam** so the desktop stays agent-ready; **the service itself is not built in Phase 11**
(Ivo's call — it is effectively a separate product surface). See [ADR-0007](adr/0007-execution-host-seam.md),
[12-database-design.md](12-database-design.md) ("SQL Server is shared metadata, not shared execution"),
[20-pipelines-deployments.md](20-pipelines-deployments.md).

## Why it is separate from the metadata DB

Phase 11's shared database gives a team one catalog/policy/audit/role definition, but every run still happens
on the user's own machine against the user's own credentials. Central execution is a bigger step: it needs a
long-running service, a trust boundary (the agent holds cloud credentials the desktop never sees), a job
protocol, streaming transport, and its own auth. Bundling that into Phase 11 would balloon the batch and
couple metadata to execution. Instead we make execution **pluggable** now and defer the agent.

## The seam: `IExecutionHost`

Today the plan/apply/state services call the process runner + coordinator directly. Phase 11 introduces a
thin abstraction so *where* a governed run executes is a policy choice, not hard-wired:

```csharp
public interface IExecutionHost
{
    ExecutionLocation Location { get; }           // Local | Agent
    Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent> output,
        CancellationToken ct);
}
```

Phase 11 ships **only** `LocalExecutionHost` (delegates to the existing runner/coordinator, so behaviour is
byte-for-byte unchanged). A future `AgentExecutionHost` will marshal the same `TerraformCommandRequest` to the
service, stream `ProcessOutputEvent`s back, and return a `ProcessResult` — the calling services do not change.
Because the **command catalogue is still the single `ArgumentList` source**, the agent runs *exactly* the
command the desktop previewed; the preview/transparency guarantee ([23](23-command-transparency.md)) holds
across the boundary.

## Trust & security model (for the future service)

- The **agent holds the credentials**; the desktop never sees them. The desktop sends a *request to run a
  specific, approved version/plan*, not secrets.
- Only **approved artefacts** run: the agent verifies the version's Git commit + config/lock hashes and only
  applies the **exact saved plan** ([ADR-0003](adr/0003-saved-plan-only-apply.md)) — the same rule, enforced
  server-side.
- **Approvals are server-enforced.** "Live may only be deployed via the agent with approval" becomes real:
  the agent refuses a run without a valid `ApprovalRequest` decided by a different `ApproveDeployment` holder.
- **Audit is authoritative** on the agent side (the desktop can't forge who-ran-what).
- Transport is mutually authenticated; job identity ties back to `IUserContext`.

## What is in Phase 11 vs the future agent

| Concern | Phase 11 (now) | Fenrix Agent (future) |
|---|---|---|
| Execution location | Local desktop only | Central agent host |
| `IExecutionHost` | seam + `LocalExecutionHost` | `AgentExecutionHost` + service |
| Credentials | local (per user) | held by the agent |
| Approvals | gate before local apply | server-enforced before agent apply |
| Terraform binary | user's, org-constrained | org-pinned on the agent |
| Audit | central metadata DB, desktop-written | agent-authoritative |

## Deferral note

No agent service, protocol implementation, or transport is written in Phase 11. The only code added is the
`IExecutionHost` abstraction and the `LocalExecutionHost` wrapper, so the governed-deploy path routes through
a seam that a later phase can point at an agent without touching the plan/apply/state services. Schedule the
service as its own phase (Phase 11-Agent / 11.5).
