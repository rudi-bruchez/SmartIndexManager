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
    public IServiceProvider? Services { get; private set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var paths = AppPaths.Default();
        Services = new ServiceCollection()
            .AddAppServices(paths.SqlScriptRoot)
            // The real password prompt (a dialog) is added in Task 3b; for read-only browsing
            // register a console-less prompt that returns null so SqlLogin connects are gated in the UI.
            .AddSingleton<IPasswordPrompt, NullPasswordPrompt>()
            .BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var theme = Services.GetRequiredService<IThemeService>();
            RequestedThemeVariant = theme.Current == ThemeVariantKind.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            desktop.MainWindow = new MainWindow { DataContext = Services.GetRequiredService<MainWindowViewModel>() };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
