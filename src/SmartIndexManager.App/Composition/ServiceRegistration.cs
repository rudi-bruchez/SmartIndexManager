using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.Providers.SqlServer;

namespace SmartIndexManager.App.Composition;

public static class ServiceRegistration
{
    // Registers the SQL Server provider factory and (in later tasks) the App services.
    // Later tasks add more registrations to this method.
    public static IServiceCollection AddAppServices(this IServiceCollection services, string scriptRoot)
    {
        services.AddSqlServerProvider(scriptRoot);
        return services;
    }
}
