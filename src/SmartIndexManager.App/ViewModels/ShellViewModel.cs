using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ShellViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly BrowseViewModel _browse;
    private readonly IThemeService _theme;

    [ObservableProperty] private NavigationDestination? _selectedDestination;
    [ObservableProperty] private object? _currentPage;
    [ObservableProperty] private bool _isDarkTheme;

    public IReadOnlyList<NavigationDestination> Destinations { get; }
    public ConnectionSessionViewModel Connection { get; }
    public PermissionStatusViewModel Permissions { get; }

    public ShellViewModel(
        ConnectionSessionViewModel connection, BrowseViewModel browse,
        PermissionStatusViewModel permissions, IThemeService theme, ILocalizer loc)
    {
        Connection = connection;
        _browse = browse;
        Permissions = permissions;
        _theme = theme;
        IsDarkTheme = _theme.Current == ThemeVariantKind.Dark;

        Destinations =
        [
            new NavigationDestination(loc["Nav_Browse"], MaterialIconKind.DatabaseSearch, browse),
            new NavigationDestination(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline,
                new PlaceholderPageViewModel(loc["Nav_Basket"], MaterialIconKind.DeleteSweepOutline, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Restore"], MaterialIconKind.BackupRestore,
                new PlaceholderPageViewModel(loc["Nav_Restore"], MaterialIconKind.BackupRestore, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Audit"], MaterialIconKind.History,
                new PlaceholderPageViewModel(loc["Nav_Audit"], MaterialIconKind.History, loc["Placeholder_Message"])),
            new NavigationDestination(loc["Nav_Settings"], MaterialIconKind.CogOutline,
                new PlaceholderPageViewModel(loc["Nav_Settings"], MaterialIconKind.CogOutline, loc["Placeholder_Message"])),
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
