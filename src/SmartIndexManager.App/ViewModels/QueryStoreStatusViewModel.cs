using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class QueryStoreStatusViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private string _database = "";
    private readonly ILocalizer _loc;

    [ObservableProperty] private QueryStoreState _state;
    [ObservableProperty] private bool _canEnable;
    [ObservableProperty] private string _label = "";

    public QueryStoreStatusViewModel(ILocalizer loc) => _loc = loc;

    public void SetProvider(IIndexProvider provider, string database)
    {
        _provider = provider;
        _database = database;
    }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        State = await _provider.GetQueryStoreStateAsync(_database, cancellationToken).ConfigureAwait(true);
        CanEnable = _provider.Capabilities.SupportsQueryStore
                 && _provider.Permissions.CanAlter
                 && State == QueryStoreState.Off;
        Label = string.Format(_loc["QueryStore_Status"], State);
    }

    [RelayCommand]
    private async Task EnableAsync(CancellationToken cancellationToken)
    {
        if (!CanEnable || _provider is null) return;
        await _provider.EnableQueryStoreAsync(_database, new QueryStoreSettings(), cancellationToken).ConfigureAwait(true);
        await LoadAsync(cancellationToken).ConfigureAwait(true);
    }
}
