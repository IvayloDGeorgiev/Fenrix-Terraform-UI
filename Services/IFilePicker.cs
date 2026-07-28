namespace Fenrix_Terraform_UI.Services;

/// <summary>
/// Picks a single existing file from the OS. Backed by the native Windows file-open picker; on platforms
/// without one it returns null and the UI falls back to a typed path. Used to select a private key to import.
/// See docs/28-key-pair-management.md.
/// </summary>
public interface IKeyFilePicker
{
    /// <summary>True when a native file picker is available on this platform.</summary>
    bool IsSupported { get; }

    /// <summary>Shows the picker and returns the chosen absolute path, or null if cancelled/unsupported.</summary>
    Task<string?> PickFileAsync();
}
