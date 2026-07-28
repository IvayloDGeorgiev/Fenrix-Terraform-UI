namespace Fenrix_Terraform_UI.Services;

/// <summary>
/// Native file picker for selecting a private key to import. On Windows it uses the WinRT
/// <c>FileOpenPicker</c> initialised against the app window; elsewhere it reports unsupported so the UI
/// offers a typed path instead. Named to avoid colliding with <c>Microsoft.Maui.Storage.FilePicker</c>.
/// </summary>
public sealed class KeyFilePicker : IKeyFilePicker
{
#if WINDOWS
    public bool IsSupported => true;

    public async Task<string?> PickFileAsync()
    {
        var picker = new Windows.Storage.Pickers.FileOpenPicker
        {
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        // Private keys come in many shapes; allow everything plus the common named types.
        picker.FileTypeFilter.Add("*");

        var mauiWindow = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
        var platformWindow = mauiWindow?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
        if (platformWindow is null)
            return null;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(platformWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
#else
    public bool IsSupported => false;

    public Task<string?> PickFileAsync() => Task.FromResult<string?>(null);
#endif
}
