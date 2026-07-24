namespace Fenrix.IaCStudio.Application.Abstractions.Files;

/// <summary>
/// Sends files/folders to the OS Recycle Bin where supported (Windows), so deletes are recoverable
/// outside Fenrix too. Falls back to a managed trash folder on other platforms. See docs/04-filesystem-sync.md.
/// </summary>
public interface IRecycleBin
{
    /// <summary>True when a real OS Recycle Bin is available on this platform.</summary>
    bool IsOsRecycleBinAvailable { get; }

    /// <summary>Sends the path to the Recycle Bin (or fallback trash). No-op if the path does not exist.</summary>
    Task SendToRecycleBinAsync(string fullPath, CancellationToken ct = default);
}
