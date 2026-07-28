using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// The guided import assistant, in two modes:
/// <list type="bullet">
/// <item><b>CLI import</b> — <c>terraform import ADDRESS ID</c> writes an existing object into state directly.
/// State-changing, so it is confirmed, locked, blocked when unbound, and history-recorded.</item>
/// <item><b>Config generation</b> (Terraform 1.5+) — an <c>import{}</c> block is written to config and
/// <c>terraform plan -generate-config-out=&lt;file&gt;</c> scaffolds HCL for the resource. This changes no state
/// (it is a plan), so it is not locked; the generated file is version-controlled via file history and
/// reviewed before a normal apply.</item>
/// </list>
/// See docs/22-terraform-files-model.md, docs/06-plan-apply-safety.md.
/// </summary>
public interface ITerraformImportService
{
    /// <summary>
    /// Resolves context and builds the redacted preview for an import. When
    /// <see cref="ImportOptions.GenerateConfigOut"/> is set the preview shows the
    /// <c>plan -generate-config-out</c> command; otherwise it shows <c>terraform import</c>. Side-effect-free.
    /// </summary>
    Task<StateOpContext> PrepareAsync(
        Guid projectId, Guid environmentId, ImportOptions options, CancellationToken ct = default);

    /// <summary>
    /// Executes the import described by <paramref name="context"/>. For CLI import: verifies the typed
    /// confirmation, acquires the lock, runs <c>terraform import</c>, and records redacted history. For config
    /// generation: writes the <c>import{}</c> block to a Fenrix-managed file, runs the generating plan, and
    /// returns the scaffolded HCL for review.
    /// </summary>
    Task<ImportResult> ExecuteAsync(
        StateOpContext context, ApplyConfirmation confirmation, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
}
