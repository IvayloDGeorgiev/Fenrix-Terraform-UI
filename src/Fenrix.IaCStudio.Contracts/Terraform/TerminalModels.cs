namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// How to start an embedded terminal session (Phase 12). The shell is launched under a Win32 pseudo-console
/// (ConPTY) in the given working directory, so any installed command — including interactive ones and
/// <c>terraform</c> subcommands not covered by a typed screen — can be run. See docs/05-terraform-engine.md.
/// </summary>
/// <param name="Shell">Full path or PATH-resolvable name of the shell to launch (e.g. <c>powershell.exe</c>, <c>cmd.exe</c>).</param>
/// <param name="WorkingDirectory">Directory the shell starts in (typically the environment's working dir).</param>
/// <param name="Columns">Initial pseudo-console width in character cells.</param>
/// <param name="Rows">Initial pseudo-console height in character cells.</param>
/// <param name="Environment">Extra environment variables to add for the shell (e.g. bound cloud credentials). May be null.</param>
public sealed record TerminalStartInfo(
    string Shell,
    string WorkingDirectory,
    int Columns,
    int Rows,
    IReadOnlyDictionary<string, string>? Environment = null);
