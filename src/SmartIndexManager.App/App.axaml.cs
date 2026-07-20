using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // The main window and its DI-resolved ViewModel are wired in Task 13.
        base.OnFrameworkInitializationCompleted();
    }
}
