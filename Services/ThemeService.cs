using Fenrix.IaCStudio.Application.Settings;
using Microsoft.JSInterop;

namespace Fenrix_Terraform_UI.Services;

/// <summary>
/// UI-layer service that resolves, applies (via JS interop) and persists the theme
/// and reduced-motion preferences. See docs/24-visual-design-language.md.
/// </summary>
public sealed class ThemeService(IJSRuntime js, ISettingsService settings)
{
    private readonly IJSRuntime _js = js;
    private readonly ISettingsService _settings = settings;

    public string Theme { get; private set; } = "dark";         // light | dark
    public string ResolvedTheme { get; private set; } = "dark"; // light | dark

    public event Action? Changed;

    /// <summary>Loads the saved theme and applies it. Call once after first render.</summary>
    public async Task InitializeAsync()
    {
        Theme = await _settings.GetOrDefaultAsync(FenrixSettingKeys.Theme, "dark");
        var reduced = await _settings.GetOrDefaultAsync(FenrixSettingKeys.ReducedMotion, false);
        await _js.InvokeVoidAsync("fenrix.setReducedMotion", reduced);
        await ApplyAsync(Theme, persist: false);
    }

    /// <summary>Toggles between dark and light, persisting the choice.</summary>
    public Task ToggleAsync() => SetThemeAsync(Theme == "light" ? "dark" : "light");

    public async Task SetThemeAsync(string theme)
    {
        await ApplyAsync(theme, persist: true);
    }

    private async Task ApplyAsync(string theme, bool persist)
    {
        Theme = theme;
        ResolvedTheme = await _js.InvokeAsync<string>("fenrix.applyTheme", theme);
        if (persist)
            await _settings.SetAsync(FenrixSettingKeys.Theme, theme);
        Changed?.Invoke();
    }
}
