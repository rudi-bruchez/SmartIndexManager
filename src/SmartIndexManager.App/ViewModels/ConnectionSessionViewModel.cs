using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionSessionViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IIndexLoadService _load;
    private readonly IPasswordPrompt _prompt;
    private readonly IDialogService _dialogs;
    private readonly ILocalizer _loc;
    private CancellationTokenSource? _cts;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string? _statusMessage;

    public IIndexProvider? ActiveProvider { get; private set; }
    public ConnectionManagerViewModel Connections { get; }

    public event Func<LoadResult, Task>? Connected;
    public event Func<Task>? Disconnected;

    public ConnectionSessionViewModel(
        IIndexLoadService load, IPasswordPrompt prompt,
        ConnectionManagerViewModel connections, IDialogService dialogs, ILocalizer loc)
    {
        _load = load;
        _prompt = prompt;
        _dialogs = dialogs;
        _loc = loc;
        Connections = connections;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var profile = Connections.Selected;
        if (profile is null) return;

        var password = profile.Auth == AuthMode.SqlLogin
            ? await _prompt.RequestAsync(profile.Name, CancellationToken.None).ConfigureAwait(true)
            : null;
        if (profile.Auth == AuthMode.SqlLogin && password is null) return;

        await TearDownAsync().ConfigureAwait(true);

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = _loc["Action_Connect"];
        try
        {
            var result = await _load.LoadAsync(profile, password, Connections.SelectedDatabases, _cts.Token).ConfigureAwait(true);
            ActiveProvider = result.Provider;
            IsConnected = true;
            StatusMessage = result.Server.ServerName;
            if (Connected is not null) await Connected(result).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = _loc["Action_Cancel"];
        }
        catch (Exception)
        {
            StatusMessage = _loc["Connection_Error"];
        }
        finally
        {
            IsBusy = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync() => await TearDownAsync().ConfigureAwait(true);

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task ManageAsync() => await _dialogs.ShowConnectionManagerAsync(Connections).ConfigureAwait(true);

    private async Task TearDownAsync()
    {
        if (ActiveProvider is null) { IsConnected = false; return; }
        if (Disconnected is not null) await Disconnected().ConfigureAwait(true);
        await ActiveProvider.DisposeAsync().ConfigureAwait(true);
        ActiveProvider = null;
        IsConnected = false;
    }

    public async ValueTask DisposeAsync() => await TearDownAsync().ConfigureAwait(true);
}
