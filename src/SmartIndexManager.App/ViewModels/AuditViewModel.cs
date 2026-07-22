using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Audit;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class AuditViewModel : ViewModelBase
{
    private readonly IAppPaths _paths;

    public ObservableCollection<AuditEntry> Entries { get; } = [];

    [ObservableProperty] private string _filter = "";

    public AuditViewModel(IAppPaths paths, ILocalizer loc) => _paths = paths;

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        Entries.Clear();
        var path = Path.Combine(_paths.ConfigDir, "audit.jsonl");
        foreach (var e in AuditLog.Read(path))
            Entries.Add(e);
        return Task.CompletedTask;
    }
}
