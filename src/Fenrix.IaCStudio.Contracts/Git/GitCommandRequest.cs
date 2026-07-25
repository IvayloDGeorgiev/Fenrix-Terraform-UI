using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// A fully-resolved request to run one <c>git</c> command. <see cref="Arguments"/> is passed verbatim to
/// <c>ProcessStartInfo.ArgumentList</c> — never concatenated into a shell string — so the preview and the
/// execution are generated from the same source (the command catalog). Mirrors the Terraform command
/// request. See docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public sealed record GitCommandRequest(
    Guid ProjectId,
    GitCommandKind Kind,
    string ExecutablePath,
    string WorkingDirectory,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    GitOperationRisk Risk);
