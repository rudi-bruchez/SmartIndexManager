using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class IndexGridViewModel : ViewModelBase
{
    private readonly List<IndexRowViewModel> _all = [];
    private readonly ILocalizer? _loc;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private IndexRowViewModel? _selectedRow;

    public DataGridCollectionView View { get; }

    public IndexGridViewModel(ILocalizer? loc = null)
    {
        _loc = loc;
        View = new DataGridCollectionView(_all) { Filter = Matches };
    }

    public int VisibleCount => View.Cast<object>().Count();

    public int TotalCount => _all.Count;

    public string MatchCountText => _loc is not null
        ? string.Format(_loc["Grid_MatchCount"], VisibleCount, TotalCount)
        : $"{VisibleCount} of {TotalCount}";

    public bool IsFiltered => FilterText.Length > 0;

    public bool HasVisibleRows => VisibleCount > 0;

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    public void SetRows(IReadOnlyList<IndexRowViewModel> rows)
    {
        _all.Clear();
        _all.AddRange(rows);
        View.Refresh();
        NotifyCounts();
    }

    partial void OnFilterTextChanged(string value)
    {
        View.Refresh();
        NotifyCounts();
    }

    private void NotifyCounts()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(MatchCountText));
        OnPropertyChanged(nameof(IsFiltered));
        OnPropertyChanged(nameof(HasVisibleRows));
    }

    private bool Matches(object item)
    {
        if (FilterText is not { Length: > 0 }) return true;
        var r = (IndexRowViewModel)item;
        return Contains(r.Database) || Contains(r.Schema) || Contains(r.Table) || Contains(r.Name);

        bool Contains(string s) => s.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }
}
