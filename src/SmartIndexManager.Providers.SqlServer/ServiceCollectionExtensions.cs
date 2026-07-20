using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerProvider(this IServiceCollection services, string scriptRoot)
    {
        services.AddSingleton<IIndexProviderFactory>(_ => new SqlServerIndexProviderFactory(scriptRoot));
        return services;
    }
}
