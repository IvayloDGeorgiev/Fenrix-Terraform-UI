using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Starts interactive terminal sessions backed by a Win32 pseudo-console (ConPTY). This is the catch-all that
/// makes <em>every</em> installed command reachable, including interactive ones the typed screens and the
/// command builder deliberately don't run (Phase 12). Windows-only; on other platforms the implementation
/// throws <see cref="PlatformNotSupportedException"/>. See docs/05-terraform-engine.md, docs/31-release-prep.md.
/// </summary>
public interface ITerminalService
{
    /// <summary>True when an embedded terminal can be started on this platform.</summary>
    bool IsSupported { get; }

    /// <summary>Launches a shell under a pseudo-console and returns the live session.</summary>
    ITerminalSession Start(TerminalStartInfo info);
}

/// <summary>
/// A live pseudo-console session. Output is delivered as decoded text chunks (which still contain ANSI escape
/// sequences — the terminal renderer interprets them); input is written as the user types. See ITerminalService.
/// </summary>
public interface ITerminalSession : IDisposable
{
    /// <summary>Stable id (used to correlate the JS terminal component with this session).</summary>
    string Id { get; }

    /// <summary>True until the shell process exits or the session is disposed.</summary>
    bool IsRunning { get; }

    /// <summary>Raised with each decoded output chunk from the shell (raw, including ANSI sequences).</summary>
    event Action<string>? Output;

    /// <summary>Raised once when the shell process exits, with its exit code.</summary>
    event Action<int>? Exited;

    /// <summary>Writes user input to the shell's input (already UTF-8/VT-encoded by the renderer).</summary>
    void Write(string data);

    /// <summary>Resizes the pseudo-console to the given character-cell dimensions.</summary>
    void Resize(int columns, int rows);
}
