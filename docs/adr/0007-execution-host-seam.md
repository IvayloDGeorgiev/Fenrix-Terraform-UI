# ADR-0007 · Execution-host seam (agent-ready, agent deferred)

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Phase 11's roadmap includes a **remote Fenrix execution agent** and central agent-run pipelines. A real agent
is a separate service with its own trust boundary, job protocol, streaming transport, and auth — far larger
than the rest of Phase 11 and coupling metadata to execution if bundled in. Ivo's decision was to **design the
agent only** this phase and defer the service, while making sure the desktop does not calcify around local
execution.

## Decision

Introduce a thin `IExecutionHost` abstraction that a governed run goes through instead of calling the process
runner/coordinator directly:

```csharp
public interface IExecutionHost
{
    ExecutionLocation Location { get; }   // Local | Agent
    Task<ProcessResult> RunAsync(TerraformCommandRequest request,
                                 IProgress<ProcessOutputEvent> output,
                                 CancellationToken ct);
}
```

Phase 11 ships **only** `LocalExecutionHost`, which delegates to the existing runner/coordinator so behaviour
is byte-for-byte unchanged. A future `AgentExecutionHost` marshals the **same** `TerraformCommandRequest` to
the service and streams the **same** `ProcessOutputEvent`s back. Because the command catalogue remains the
**single `ArgumentList` source**, the agent runs exactly the previewed command — the transparency guarantee
([23-command-transparency.md](../23-command-transparency.md)) and the saved-plan-only rule
([ADR-0003](0003-saved-plan-only-apply.md)) hold across the boundary (enforced server-side on the agent). Full
design, trust model, and the Phase-11-vs-future split are in [30-fenrix-agent.md](../30-fenrix-agent.md).

## Consequences

**Positive.** The governed-deploy path is already routed through a seam, so a later phase points it at an agent
without changing plan/apply/state services; no premature protocol/transport code; the desktop stays a thin
client conceptually. **Negative / mitigations.** A one-implementation interface is mild over-abstraction now →
justified by the concrete, roadmapped agent and kept trivially thin. **Rejected alternatives.** *Build a minimal
agent now* (too large, risky with no compile/run in the sandbox); *no seam, refactor later* (would force touching
every execution call site under a future deadline).
