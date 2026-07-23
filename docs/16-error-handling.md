# 16 · Error Handling

Every tool operation returns a **structured result** rather than throwing raw exceptions across layers.

```csharp
public sealed record ToolExecutionResult(
    int ExitCode,
    ToolExecutionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    IReadOnlyList<ToolOutputEvent> Events,
    string? ErrorCode,
    string? UserMessage);
```

## Error classification

Prerequisite missing · authentication required · permission denied · invalid configuration · backend error · state locked · provider error · network failure · Git conflict · filesystem failure · operation cancelled · unsupported CLI version · unknown external-tool failure.

Classification lets the UI give a specific, recoverable message instead of a stack trace, and lets policies react (e.g. "authentication required" → prompt to run `az login`).

## What the UI shows on failure

- **What failed** — the operation in plain language.
- **Which project and environment** were involved.
- **Whether infrastructure may have changed** — critical after a partial apply.
- **Recommended recovery action** — tailored to the error class.
- **Expandable technical details** — full (redacted) output for those who want it.
- **Copy diagnostics button** — copies a redacted bundle for support.

## Principles

- Fail structured, not with raw exceptions crossing boundaries.
- Always tell the user whether real infrastructure might have changed.
- Redact everything shown or copied.
- Prefer actionable recovery guidance over generic apologies.
- Cancellation is a first-class, non-error outcome (`operation cancelled`).
