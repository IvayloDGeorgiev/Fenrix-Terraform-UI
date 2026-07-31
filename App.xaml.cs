using Fenrix.IaCStudio.Application.Abstractions.Maintenance;
using Microsoft.Extensions.DependencyInjection;

namespace Fenrix_Terraform_UI
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            InitializeComponent();
            _services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new MainPage()) { Title = "Fenrix Terraform UI" };

            // Signal a clean shutdown so the next launch doesn't treat this session as a crash (Phase 12).
            // Best-effort; if the process is killed the marker survives and crash recovery kicks in.
            window.Destroying += (_, _) =>
            {
                try { _services.GetService<IBackupService>()?.EndSession(); }
                catch { /* never block teardown */ }
            };

            return window;
        }
    }
}
