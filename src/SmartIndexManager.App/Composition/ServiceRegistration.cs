using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Providers.SqlServer;

namespace SmartIndexManager.App.Composition;

public static class ServiceRegistration
{
    // Registers the SQL Server provider factory and (in later tasks) the App services.
    // Later tasks add more registrations to this method.
    public static IServiceCollection AddAppServices(this IServiceCollection services, string scriptRoot)
    {
        services.AddSqlServerProvider(scriptRoot);
        services.AddSingleton<IAppPaths>(_ => AppPaths.Default());
        services.AddSingleton<ILocalizer, ResxLocalizer>();
        return services;
    }
}
