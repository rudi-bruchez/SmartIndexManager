using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.App.Views;

namespace SmartIndexManager.App.Services;

public sealed class AvaloniaDialogService : IDialogService, IPasswordPrompt
{
    public async Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return;

        var dialog = new ConnectionManagerDialog { DataContext = vm };
        await dialog.ShowDialog(desktop.MainWindow);
    }

    public async Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow is null)
            return null;

        var vm = new PasswordPromptViewModel(connectionName);
        var dialog = new PasswordPromptWindow { DataContext = vm };

        _ = vm.Result.Task.ContinueWith(
            _ => Dispatcher.UIThread.Post(dialog.Close),
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

        await dialog.ShowDialog(desktop.MainWindow);

        if (!vm.Result.Task.IsCompleted)
            vm.Result.TrySetResult(null);

        return await vm.Result.Task;
    }
}
