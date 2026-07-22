using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public enum BrowseState { Disconnected, Loading, Ready, Empty, Error }

public sealed partial class BrowseViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;
    private readonly SemaphoreSlim _detailGate = new(1, 1);
    private CancellationTokenSource? _detailCts;
    private IndexDetailViewModel? _detail;

    [ObservableProperty] private BrowseState _state = BrowseState.Disconnected;
    [ObservableProperty] private string? _errorMessage;

    public IndexGridViewModel Grid { get; }
    public DeletionBasketViewModel Basket { get; }

    public IndexDetailViewModel? Detail
    {
        get => _detail;
        private set { _detail = value; OnPropertyChanged(nameof(Detail)); }
    }

    public BrowseViewModel(IndexGridViewModel grid, DeletionBasketViewModel basket, IAppPaths paths, ILocalizer loc)
    {
        Grid = grid;
        Basket = basket;
        _paths = paths;
        _loc = loc;
        Grid.PropertyChanged += OnGridPropertyChanged;
    }

    [RelayCommand(CanExecute = nameof(CanAddSelectedToBasket))]
    private void AddSelectedToBasket()
    {
        var row = Grid.SelectedRow;
        if (row is null || row.NotDeletable) return;
        Basket.Add(row.Index, row.Safety, row.ScoreDetail);
    }

    private bool CanAddSelectedToBasket() => Grid.SelectedRow is { NotDeletable: false };

    private void OnGridPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(IndexGridViewModel.SelectedRow))
        {
            AddSelectedToBasketCommand.NotifyCanExecuteChanged();
            _ = ShowDetailAsync(Grid.SelectedRow);
        }
    }

    public async Task OnConnectedAsync(IIndexProvider provider, IReadOnlyList<IndexRowViewModel> rows, CancellationToken ct)
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        Basket.SetProvider(provider);
        Detail = new IndexDetailViewModel(provider, _paths, _loc);
        Grid.SetRows(rows);
        ErrorMessage = null;
        State = rows.Count > 0 ? BrowseState.Ready : BrowseState.Empty;
    }

    public async Task OnDisconnectedAsync()
    {
        await StopDetailWorkAsync().ConfigureAwait(true);
        Detail = null;
        Grid.SetRows([]);
        Basket.Clear();
        ErrorMessage = null;
        State = BrowseState.Disconnected;
    }

    public async Task ShowDetailAsync(IndexRowViewModel? row)
    {
        if (row is null || Detail is null) return;

        _detailCts?.Cancel();
        await _detailGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var detail = Detail;
            if (detail is null) return;

            var cts = new CancellationTokenSource();
            _detailCts = cts;
            try
            {
                await detail.ShowAsync(row, cts.Token).ConfigureAwait(true);
                ErrorMessage = null;
                State = Grid.HasVisibleRows ? BrowseState.Ready : BrowseState.Empty;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorMessage = $"{_loc["Detail_Error"]}: {ex.Message}";
                State = BrowseState.Error;
            }
            finally
            {
                cts.Dispose();
                if (ReferenceEquals(_detailCts, cts)) _detailCts = null;
            }
        }
        finally
        {
            _detailGate.Release();
        }
    }

    private async Task StopDetailWorkAsync()
    {
        _detailCts?.Cancel();
        await _detailGate.WaitAsync().ConfigureAwait(true);
        _detailGate.Release();
    }

    public async ValueTask DisposeAsync()
    {
        Grid.PropertyChanged -= OnGridPropertyChanged;
        await StopDetailWorkAsync().ConfigureAwait(true);
        _detailGate.Dispose();
    }
}
