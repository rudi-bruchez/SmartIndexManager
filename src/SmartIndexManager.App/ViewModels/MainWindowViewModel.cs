using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IIndexLoadService _load;
    private readonly IPasswordPrompt _prompt;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _detailCts;
    private Task? _detailTask;
    private IIndexProvider? _provider;
    private IndexDetailViewModel? _detail;

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;

    public ConnectionManagerViewModel Connections { get; }
    public IndexGridViewModel Grid { get; }
    public PermissionStatusViewModel Permissions { get; }

    public IndexDetailViewModel? Detail
    {
        get => _detail;
        private set { _detail = value; OnPropertyChanged(nameof(Detail)); }
    }

    public MainWindowViewModel(
        IIndexLoadService load, IPasswordPrompt prompt,
        ConnectionManagerViewModel connections, IndexGridViewModel grid,
        PermissionStatusViewModel permissions, IAppPaths paths, ILocalizer loc)
    {
        _load = load;
        _prompt = prompt;
        _paths = paths;
        _loc = loc;
        Connections = connections;
        Grid = grid;
        Permissions = permissions;

        Grid.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(IndexGridViewModel.SelectedRow))
                _ = ShowDetailAsync(Grid.SelectedRow);
        };
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var profile = Connections.Selected;
        if (profile is null) return;

        var password = profile.Auth == AuthMode.SqlLogin
            ? await _prompt.RequestAsync(profile.Name, CancellationToken.None).ConfigureAwait(true)
            : null;
        if (profile.Auth == AuthMode.SqlLogin && password is null) return;   // cancelled

        await StopDetailWorkAsync().ConfigureAwait(true);

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = _loc["Action_Connect"];
        try
        {
            await DisposeCurrentProviderAsync().ConfigureAwait(true);

            var result = await _load.LoadAsync(profile, password, Connections.SelectedDatabases, _cts.Token).ConfigureAwait(true);
            _provider = result.Provider;
            Detail = new IndexDetailViewModel(_provider, _paths, _loc);
            Grid.SetRows(result.Rows);
            Permissions.Update(result.Permissions);
            StatusMessage = result.Server.ServerName;
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

    public async Task ShowDetailAsync(IndexRowViewModel? row)
    {
        if (row is null || Detail is null) return;
        _detailCts?.Cancel();
        var cts = new CancellationTokenSource();
        _detailCts = cts;
        _detailTask = Detail.ShowAsync(row, cts.Token);
        try
        {
            await _detailTask.ConfigureAwait(true);
        }
        catch (OperationCanceledException) { }
    }

    private async Task StopDetailWorkAsync()
    {
        _detailCts?.Cancel();
        if (_detailTask is not null)
        {
            try { await _detailTask.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
            catch { }
        }
        _detailCts?.Dispose();
        _detailCts = null;
        _detailTask = null;
    }

    private async Task DisposeCurrentProviderAsync()
    {
        if (_provider is null) return;
        await _provider.DisposeAsync().ConfigureAwait(true);
        _provider = null;
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    public async ValueTask DisposeAsync()
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        await DisposeCurrentProviderAsync().ConfigureAwait(true);
    }
}
