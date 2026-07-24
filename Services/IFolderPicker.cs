namespace Fenrix_Terraform_UI.Services;

/// <summary>
/// Picks a folder from the OS. Backed by the native Windows folder picker; on platforms without one
/// it returns null and the UI falls back to a typed path. See docs/03-domain-model.md.
/// </summary>
public interface IFolderPicker
{
    /// <summary>True when a native folder picker is available on this platform.</summary>
    bool IsSupported { get; }

    /// <summary>Shows the picker and returns the chosen absolute path, or null if cancelled/unsupported.</summary>
    Task<string?> PickFolderAsync();
}
