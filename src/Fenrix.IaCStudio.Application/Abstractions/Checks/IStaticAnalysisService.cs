using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Abstractions.Checks;

/// <summary>
/// Runs the static-analysis tools (TFLint for lint/deprecations; tfsec or Trivy for security misconfiguration)
/// over an environment's working directory and returns normalised findings. Standalone and read-only: it never
/// touches the plan/apply spine, takes no environment lock, and drives each tool through the shared
/// <c>ArgumentList</c> process runner (never a shell string). See docs/34-checks.md.
/// </summary>
public interface IStaticAnalysisService
{
    /// <summary>
    /// Runs whichever of TFLint and the security scanner are installed over the environment's working
    /// directory. Tools that aren't installed are reported as unavailable rather than failing the whole run.
    /// </summary>
    /// <param name="progress">Optional human-readable progress (e.g. "Running TFLint…").</param>
    Task<StaticAnalysisReport> AnalyzeAsync(
        Guid projectId, Guid environmentId,
        IProgress<string>? progress = null, CancellationToken ct = default);
}
