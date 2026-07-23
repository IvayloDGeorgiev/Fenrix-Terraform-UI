using Fenrix.IaCStudio.Application.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Fenrix.IaCStudio.Application.DependencyInjection;

/// <summary>Registers Application-layer services (use cases, policies). See docs/01-architecture.md.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddFenrixApplication(this IServiceCollection services)
    {
        services.AddScoped<ISettingsService, SettingsService>();
        return services;
    }
}
