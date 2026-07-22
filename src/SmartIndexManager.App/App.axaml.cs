using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var paths = AppPaths.Default();
        var services = new ServiceCollection()
            .AddAppServices(paths.SqlScriptRoot)
            .BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var theme = services.GetRequiredService<IThemeService>();
            RequestedThemeVariant = theme.Current == ThemeVariantKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<ShellViewModel>() };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
