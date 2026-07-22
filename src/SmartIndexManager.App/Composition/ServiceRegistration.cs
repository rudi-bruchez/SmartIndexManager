using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Restore;
using SmartIndexManager.Core.Settings;
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
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<IAuthAvailability>(sp => AuthAvailability.ForCurrentOs(sp.GetRequiredService<ILocalizer>()));
        services.AddSingleton<IIndexLoadService, IndexLoadService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<DeletionBasket>();
        services.AddSingleton<DryRunReportBuilder>();
        services.AddSingleton<DeletionOrchestrator>(sp =>
            new DeletionOrchestrator(Path.Combine(sp.GetRequiredService<IAppPaths>().ConfigDir, "audit.jsonl")));
        services.AddSingleton<RestoreService>();

        services.AddSingleton<IPasswordPrompt, AvaloniaDialogService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();

        services.AddSingleton<ConnectionManagerViewModel>();
        services.AddSingleton<IndexGridViewModel>();
        services.AddSingleton<PermissionStatusViewModel>();
        services.AddSingleton<BrowseViewModel>();
        services.AddSingleton<DeletionBasketViewModel>();
        services.AddSingleton<DryRunViewModel>();
        services.AddSingleton<RestoreViewModel>();
        services.AddSingleton<AuditViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ConnectionSessionViewModel>();
        services.AddSingleton<ShellViewModel>();

        return services;
    }
}
