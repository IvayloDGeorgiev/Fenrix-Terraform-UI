using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Advanced, state-changing state operations: <c>state mv/rm/push</c>, <c>force-unlock</c>, and workspace
/// <c>select/new/delete</c>. Every mutation is gated behind a typed confirmation, acquires the
/// per-environment operation lock, is blocked when the environment has no bound cloud connection (the Phase 8
/// authentication-required rule), and records redacted history. Read-only helpers (<c>workspace list</c>,
/// <c>state pull</c> to a file) do not take the lock. See docs/05-terraform-engine.md,
/// docs/06-plan-apply-safety.md, docs/22-terraform-files-model.md.
/// </summary>
public interface ITerraformStateService
{
    /// <summary>
    /// Resolves context and builds the redacted preview + confirmation phrase for a state-changing operation,
    /// computing any block reason (no cloud connection, environment locked, missing input, …). Side-effect-free.
    /// </summary>
    Task<StateOpContext> PrepareAsync(TerraformRunSpec spec, CancellationToken ct = default);

    /// <summary>
    /// Runs the operation described by <paramref name="context"/> after verifying the typed confirmation and
    /// acquiring the environment lock. On success for <c>workspace select</c>, persists the environment's
    /// active workspace. Streams output and records redacted history.
    /// </summary>
    Task<StateOpResult> ExecuteAsync(
        StateOpContext context, ApplyConfirmation confirmation, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>Runs <c>workspace list</c> (read-only) and returns the parsed workspaces + current selection.</summary>
    Task<WorkspaceSnapshot> GetWorkspacesAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Runs <c>state pull</c> and writes the raw state (which can contain plaintext secrets) to
    /// <paramref name="destinationPath"/>. Read-only (no lock); the output is never logged. Returns the
    /// process outcome (and a block reason when the working dir/binary is unavailable).
    /// </summary>
    Task<StateOpResult> PullToFileAsync(
        Guid projectId, Guid environmentId, string destinationPath, CancellationToken ct = default);
}
