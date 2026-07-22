using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
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
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ILocalizer, ResxLocalizer>();
        services.AddSingleton<IAuthAvailability>(sp => AuthAvailability.ForCurrentOs(sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IIndexLoadService, IndexLoadService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddTransient<ConnectionManagerViewModel>();
        services.AddSingleton<IndexGridViewModel>();
        services.AddSingleton<PermissionStatusViewModel>();
        services.AddSingleton<IPasswordPrompt, AvaloniaDialogService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<ConnectionSessionViewModel>();
        services.AddSingleton<ShellViewModel>();
        return services;
    }
}
