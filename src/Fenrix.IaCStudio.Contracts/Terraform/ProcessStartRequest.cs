namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// A tool-agnostic request to run an external process safely. This is the shared Phase 3 process primitive
/// (it lives under the Terraform contracts namespace for historical reasons, but carries no Terraform
/// semantics): both the Terraform executor and the Git engine feed the same <c>IProcessRunner</c> through
/// this type. <see cref="Arguments"/> is passed verbatim to <c>ProcessStartInfo.ArgumentList</c> — never a
/// shell string. See docs/05-terraform-engine.md and docs/08-git-engine.md.
/// </summary>
public sealed record ProcessStartRequest(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    string CommandLabel,
    bool RequiresInteractiveTerminal = false,
    /// <summary>Optional text piped to the process's standard input, then closed (EOF). Never logged.</summary>
    string? StandardInput = null);
