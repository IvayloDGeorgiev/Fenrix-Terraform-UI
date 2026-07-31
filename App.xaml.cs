using Fenrix.IaCStudio.Application.Abstractions.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace Fenrix_Terraform_UI
{
    public partial class App : Application
    {
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage()) { Title = "Fenrix Terraform UI" };

            // Signal a clean shutdown so the next launch doesn't treat this session as a crash (Phase 12).
            // Resolved lazily (not via constructor injection) so nothing here can affect window creation.
            window.Destroying += (_, _) =>
            {
                try { IPlatformApplication.Current?.Services.GetService<IBackupService>()?.EndSession(); }
                catch { /* never block teardown */ }
            };

            return window;
        }
    }
}
