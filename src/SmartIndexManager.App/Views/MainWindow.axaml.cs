using Avalonia.Controls;

namespace SmartIndexManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (DataContext is IAsyncDisposable disposable)
        {
            try
            {
                await disposable.DisposeAsync().ConfigureAwait(true);
            }
            catch { }
        }
    }
}
