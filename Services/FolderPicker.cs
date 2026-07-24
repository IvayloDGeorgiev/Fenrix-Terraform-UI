namespace Fenrix_Terraform_UI.Services;

/// <summary>
/// Native folder picker. On Windows it uses the WinRT <c>FolderPicker</c> initialised against the
/// app window; elsewhere it reports unsupported so the UI offers a typed path instead.
/// </summary>
public sealed class FolderPicker : IFolderPicker
{
#if WINDOWS
    public bool IsSupported => true;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        var platformWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (platformWindow is null)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
#else
    public bool IsSupported => false;

    public Task<string?> PickFolderAsync() => Task.FromResult<string?>(null);
#endif
}
