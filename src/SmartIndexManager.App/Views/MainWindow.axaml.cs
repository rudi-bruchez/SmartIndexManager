using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _observed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_observed is not null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
        _observed = DataContext as MainWindowViewModel;
        if (_observed is not null)
        {
            _observed.PropertyChanged += OnViewModelPropertyChanged;
            ApplyTheme(_observed.IsDarkTheme);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsDarkTheme) && _observed is not null)
            ApplyTheme(_observed.IsDarkTheme);
    }

    private static void ApplyTheme(bool dark)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    protected override async void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_observed is not null) _observed.PropertyChanged -= OnViewModelPropertyChanged;
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
