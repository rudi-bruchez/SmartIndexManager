using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Audit;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class AuditViewModel : ViewModelBase
{
    private readonly IAppPaths _paths;
    private IReadOnlyList<AuditEntry> _all = [];

    public ObservableCollection<AuditEntry> Entries { get; } = [];

    [ObservableProperty] private string _filter = "";

    public AuditViewModel(IAppPaths paths) => _paths = paths;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        var path = Path.Combine(_paths.ConfigDir, "audit.jsonl");
        _all = AuditLog.Read(path);
        ApplyFilter();
        return Task.CompletedTask;
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Entries.Clear();
        foreach (var e in _all.Where(Matches))
            Entries.Add(e);
    }

    private bool Matches(AuditEntry e)
    {
        if (string.IsNullOrWhiteSpace(Filter)) return true;
        return e.Action.ToString().Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || e.Server.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || e.Database.Contains(Filter, StringComparison.OrdinalIgnoreCase)
            || e.Detail.Contains(Filter, StringComparison.OrdinalIgnoreCase);
    }
}
