using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly BrowseViewModel _browse;
    private readonly DeletionBasketViewModel _basket;
    private readonly RestoreViewModel _restore;
    private readonly AuditViewModel _audit;
    private readonly SettingsViewModel _settings;
    private readonly IThemeService _theme;
    private readonly ILocalizer _loc;

    [ObservableProperty] private NavigationDestination? _selectedDestination;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isDarkTheme;

    public IReadOnlyList<NavigationDestination> Destinations { get; }
    public ConnectionSessionViewModel Connection { get; }
    public PermissionStatusViewModel Permissions { get; }

    public ShellViewModel(
        ConnectionSessionViewModel connection, BrowseViewModel browse,
        DeletionBasketViewModel basket, RestoreViewModel restore,
        AuditViewModel audit, SettingsViewModel settings,
        PermissionStatusViewModel permissions, IThemeService theme, ILocalizer loc)
    {
        Connection = connection;
        _browse = browse;
        _basket = basket;
        _restore = restore;
        _audit = audit;
        _settings = settings;
        Permissions = permissions;
        _theme = theme;
        _loc = loc;
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;

        Destinations =
        [
            new NavigationDestination(loc["Nav_Browse"], MaterialIconKind.DatabaseSearch, browse),
            new NavigationDestination(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline, basket),
            new NavigationDestination(loc["Nav_Restore"], MaterialIconKind.BackupRestore, restore),
            new NavigationDestination(loc["Nav_Audit"], MaterialIconKind.History, audit),
            new NavigationDestination(loc["Nav_Settings"], MaterialIconKind.CogOutline, settings),
        ];

        Connection.Connected += OnConnectedAsync;
        Connection.Disconnected += OnDisconnectedAsync;

        SelectedDestination = Destinations[0];
    }

    partial void OnSelectedDestinationChanged(NavigationDestination? value)
        => CurrentPage = value?.PageViewModel;

    private async Task OnConnectedAsync(LoadResult result)
    {
        Permissions.Update(result.Permissions);
        _basket.SetProvider(result.Provider);
        _restore.SetProvider(result.Provider);
        Permissions.QueryStore = new QueryStoreStatusViewModel(_loc);
        Permissions.QueryStore.SetProvider(result.Provider, result.Rows.FirstOrDefault()?.Database ?? "");
        await Permissions.QueryStore.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await _audit.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        await _browse.OnConnectedAsync(result.Provider, result.Rows, CancellationToken.None).ConfigureAwait(true);
        SelectedDestination = Destinations[0];
    }

    private async Task OnDisconnectedAsync() => await _browse.OnDisconnectedAsync().ConfigureAwait(true);

    [RelayCommand]
    private void ToggleTheme()
    {
        _theme.Toggle();
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;
    }

    public async ValueTask DisposeAsync()
    {
        Connection.Connected -= OnConnectedAsync;
        Connection.Disconnected -= OnDisconnectedAsync;
        await Connection.DisposeAsync().ConfigureAwait(true);
        await _browse.DisposeAsync().ConfigureAwait(true);
    }
}
