namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>A single labelled contextual chip shown beside a command preview (working dir, version, cloud…).</summary>
public sealed record CommandContextChip(string Label, string Value);

/// <summary>
/// A read-only, redacted preview of the exact command that will run. Built from the same argument list
/// the runner executes, so preview and execution never diverge. The <see cref="DisplayCommand"/> is
/// safe to render and copy — secrets are already reduced to named references. See
/// docs/23-command-transparency.md.
/// </summary>
public sealed record CommandPreview(
    string ExecutablePath,
    string ExecutableDisplayName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyList<CommandContextChip> Chips,
    string DisplayCommand);
