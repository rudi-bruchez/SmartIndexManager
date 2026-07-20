using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class IndexGridViewModel : ViewModelBase
{
    private readonly List<IndexRowViewModel> _all = [];

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private IndexRowViewModel? _selectedRow;

    public DataGridCollectionView View { get; }

    public IndexGridViewModel()
    {
        View = new DataGridCollectionView(_all) { Filter = Matches };
    }

    public int VisibleCount => View.Cast<object>().Count();

    public void SetRows(IReadOnlyList<IndexRowViewModel> rows)
    {
        _all.Clear();
        _all.AddRange(rows);
        View.Refresh();
    }

    partial void OnFilterTextChanged(string value) => View.Refresh();

    private bool Matches(object item)
    {
        if (FilterText is not { Length: > 0 }) return true;
        var r = (IndexRowViewModel)item;
        return Contains(r.Database) || Contains(r.Schema) || Contains(r.Table) || Contains(r.Name);

        bool Contains(string s) => s.Contains(FilterText, StringComparison.OrdinalIgnoreCase);
    }
}
