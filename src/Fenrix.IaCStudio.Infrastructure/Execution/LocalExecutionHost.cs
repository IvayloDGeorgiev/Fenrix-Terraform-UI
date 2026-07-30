using Fenrix.IaCStudio.Application.Abstractions.Execution;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Infrastructure.Execution;

/// <summary>
/// The only <see cref="IExecutionHost"/> shipped in Phase 11: runs the request on the local machine via the
/// shared process runner, so behaviour is byte-for-byte the same as calling the runner directly. A future
/// <c>AgentExecutionHost</c> will implement the same interface to route governed runs to the Fenrix Agent
/// without changing any caller. See docs/30-fenrix-agent.md, ADR-0007.
/// </summary>
public sealed class LocalExecutionHost(IProcessRunner runner) : IExecutionHost
{
    private readonly IProcessRunner _runner = runner;

    public ExecutionLocation Location => ExecutionLocation.Local;

    public Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken cancellationToken = default)
        => _runner.RunAsync(request, output, cancellationToken);
}
