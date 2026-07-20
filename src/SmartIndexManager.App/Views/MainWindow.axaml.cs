using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        WireSelection();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm && vm.CurrentProvider is not null)
            vm.CurrentProvider.DisposeAsync().AsTask().ConfigureAwait(false);
        base.OnClosing(e);
    }

    private void WireSelection()
    {
        if (GridView.DataContext is not IndexGridViewModel grid)
            return;

        grid.PropertyChanged += async (_, args) =>
        {
            if (args.PropertyName != nameof(IndexGridViewModel.SelectedRow))
                return;
            if (GridView.DataContext is not IndexGridViewModel g || g.SelectedRow is null)
                return;
            if (DataContext is not MainWindowViewModel vm || vm.CurrentProvider is null)
                return;
            if (App.Current is not App app || app.Services is null)
                return;

            var detail = new IndexDetailViewModel(
                vm.CurrentProvider,
                app.Services.GetRequiredService<IAppPaths>(),
                app.Services.GetRequiredService<ILocalizer>());
            Detail.DataContext = detail;
            await detail.ShowAsync(g.SelectedRow, default);
        };
    }
}
