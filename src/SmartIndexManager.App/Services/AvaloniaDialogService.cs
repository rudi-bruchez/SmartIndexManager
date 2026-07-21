using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    public async Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var dialog = new ConnectionManagerDialog { DataContext = vm };
        await dialog.ShowDialog(desktop.MainWindow);
    }
}
