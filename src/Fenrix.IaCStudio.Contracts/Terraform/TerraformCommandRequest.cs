using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// A fully-resolved request to run one Terraform command. The <see cref="Arguments"/> list is passed
/// verbatim to <c>ProcessStartInfo.ArgumentList</c> — never concatenated into a shell string — so the
/// preview and the execution are generated from the same source. See docs/05-terraform-engine.md and
/// docs/23-command-transparency.md.
/// </summary>
public sealed record TerraformCommandRequest(
    Guid ProjectId,
    Guid EnvironmentId,
    TerraformCommandKind Kind,
    string ExecutablePath,
    string WorkingDirectory,
    string Command,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> EnvironmentVariables,
    TerraformRiskLevel RiskLevel,
    bool RequiresInteractiveTerminal = false);
