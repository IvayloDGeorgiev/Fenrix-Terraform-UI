using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Execution;

/// <summary>
/// Where a governed Terraform run executes. Phase 11 ships only <c>LocalExecutionHost</c> (delegates to the
/// existing runner/coordinator, byte-for-byte unchanged); a future <c>AgentExecutionHost</c> marshals the same
/// request to the Fenrix Agent and streams the same events back. The command catalogue stays the single
/// ArgumentList source, so the agent runs exactly the previewed command. Design only this phase — see
/// docs/30-fenrix-agent.md, ADR-0007.
/// </summary>
public interface IExecutionHost
{
    /// <summary>Local or Agent. Always <see cref="ExecutionLocation.Local"/> in Phase 11.</summary>
    ExecutionLocation Location { get; }

    /// <summary>Runs a fully-built command request, streaming output events, and returns the process result.</summary>
    Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken cancellationToken = default);
}
