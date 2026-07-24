using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.DependencyInjection;
using Fenrix.IaCStudio.Infrastructure.DependencyInjection;
using Fenrix_Terraform_UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix_Terraform_UI
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();

            // Fenrix layers (see docs/01-architecture.md).
            builder.Services.AddFenrixApplication();
            builder.Services.AddFenrixInfrastructure();

            // UI-layer services.
            builder.Services.AddScoped<ThemeService>();
            builder.Services.AddSingleton<IFolderPicker, FolderPicker>();

#if DEBUG
            builder.Services.AddBlazorWebViewDeveloperTools();
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();

            // One-time startup: create the workspace tree and ensure the database exists.
            // Kept synchronous-safe here; see docs/12-database-design.md.
            var initializer = app.Services.GetRequiredService<IAppInitializer>();
            initializer.InitializeAsync().GetAwaiter().GetResult();

            return app;
        }
    }
}
