namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>Which stream a line of process output came from.</summary>
public enum OutputStream
{
    Stdout = 0,
    Stderr = 1
}

/// <summary>
/// One structured line of process output, streamed to the UI as it arrives. Delivered via
/// <c>IProgress&lt;ProcessOutputEvent&gt;</c> so rendering stays on the UI thread. See
/// docs/05-terraform-engine.md.
/// </summary>
public sealed record ProcessOutputEvent(OutputStream Stream, string Text, DateTimeOffset Timestamp)
{
    public static ProcessOutputEvent Out(string text) => new(OutputStream.Stdout, text, DateTimeOffset.Now);
    public static ProcessOutputEvent Error(string text) => new(OutputStream.Stderr, text, DateTimeOffset.Now);
}
