namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// One Terraform subcommand as discovered from <c>terraform -help</c> (Phase 12 dynamic command builder).
/// </summary>
/// <param name="Name">The subcommand (e.g. <c>plan</c>, <c>state</c>, <c>providers</c>).</param>
/// <param name="Synopsis">The one-line description from the help listing.</param>
/// <param name="IsCommon">True for the "main"/common commands Terraform lists first.</param>
/// <param name="IsBlocked">
/// True when Fenrix routes this command to a dedicated safe screen instead of the builder (mutating/guarded).
/// </param>
/// <param name="RedirectReason">Why it's blocked + which screen to use, when <paramref name="IsBlocked"/>.</param>
/// <param name="RedirectRoute">Project-relative route of the safe screen, when <paramref name="IsBlocked"/>.</param>
public sealed record TerraformCommandInfo(
    string Name,
    string Synopsis,
    bool IsCommon,
    bool IsBlocked,
    string? RedirectReason,
    string? RedirectRoute);

/// <summary>A single option/flag parsed from a command's <c>-help</c> output.</summary>
/// <param name="Name">The flag without its leading dash (e.g. <c>upgrade</c>, <c>var-file</c>).</param>
/// <param name="TakesValue">True when the flag expects a value (rendered as <c>-flag=value</c>).</param>
/// <param name="Description">The help text for the flag.</param>
/// <param name="ValueHint">A placeholder for the value input (e.g. <c>PATH</c>, <c>KEY=VALUE</c>), if known.</param>
public sealed record TerraformFlagInfo(
    string Name,
    bool TakesValue,
    string Description,
    string? ValueHint);

/// <summary>Result of discovering the installed command set: the commands, or a reason Fenrix can't list them.</summary>
/// <param name="Commands">The parsed command list (empty when <paramref name="BlockReason"/> is set).</param>
/// <param name="BlockReason">Why discovery failed (no binary, version constraint, etc.), or null on success.</param>
/// <param name="TerraformVersion">The resolved Terraform version, when available.</param>
public sealed record CommandCatalogResult(
    IReadOnlyList<TerraformCommandInfo> Commands,
    string? BlockReason,
    string? TerraformVersion)
{
    public bool Available => BlockReason is null;
}

/// <summary>Per-command help: synopsis, usage line, and parsed flags. Backs the dynamic form.</summary>
/// <param name="Name">The subcommand.</param>
/// <param name="Synopsis">Its description (first non-usage paragraph).</param>
/// <param name="Usage">The <c>Usage: terraform …</c> line, if present.</param>
/// <param name="Flags">The parsed options.</param>
/// <param name="RawHelp">The full captured help text (shown verbatim as a reference).</param>
public sealed record TerraformCommandHelp(
    string Name,
    string Synopsis,
    string? Usage,
    IReadOnlyList<TerraformFlagInfo> Flags,
    string RawHelp);
