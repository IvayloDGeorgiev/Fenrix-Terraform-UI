using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Discovers the installed Terraform command set and per-command help by running <c>terraform -help</c> and
/// <c>terraform &lt;cmd&gt; -help</c> through the normal executor spine, then parsing the output (Phase 12
/// dynamic command builder). Read-only; used to populate the command builder so every installed command is
/// reachable. See docs/05-terraform-engine.md, docs/23-command-transparency.md.
/// </summary>
public interface ITerraformHelpService
{
    /// <summary>
    /// Lists the installed Terraform commands (with synopses and Fenrix's block/redirect classification), or a
    /// block reason if the binary/version can't be resolved for this project+environment.
    /// </summary>
    Task<CommandCatalogResult> GetCommandsAsync(Guid projectId, Guid environmentId, CancellationToken ct = default);

    /// <summary>
    /// Fetches and parses the help for a single command (synopsis, usage, flags). Returns null if the command
    /// can't be run (no binary) — the caller shows the discovery block reason in that case.
    /// </summary>
    Task<TerraformCommandHelp?> GetCommandHelpAsync(
        Guid projectId, Guid environmentId, string command, CancellationToken ct = default);
}
