using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Abstractions.Checks;

/// <summary>
/// Resolves the external check-tool binaries (TFLint, tfsec, Trivy, Infracost). Resolution prefers the
/// configured executable (Settings <c>checks.&lt;tool&gt;.executable</c>, project scope first) and falls back to
/// the system <c>PATH</c> — mirroring <c>ITerraformDiscovery</c>. See docs/34-checks.md.
/// </summary>
public interface ICheckToolDiscovery
{
    /// <summary>Resolves one tool's status (installed/where/version), or a "missing" status when not found.</summary>
    Task<CheckToolStatus> ResolveAsync(CheckTool tool, Guid? projectId = null, CancellationToken ct = default);

    /// <summary>Resolves the status of every check tool.</summary>
    Task<IReadOnlyList<CheckToolStatus>> ResolveAllAsync(Guid? projectId = null, CancellationToken ct = default);
}
