using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Restore;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class RestoreViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    public ObservableCollection<RestoreSessionViewModel> Sessions { get; } = [];

    [ObservableProperty] private string? _statusMessage;

    public RestoreViewModel(IAppPaths paths, ILocalizer loc)
    {
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider) => _provider = provider;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        Sessions.Clear();
        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_paths.DefaultBackupRoot, _provider.ServerInfo.ServerName, cancellationToken).ConfigureAwait(true);
        foreach (var s in sessions)
            Sessions.Add(new RestoreSessionViewModel(s));
    }

    [RelayCommand]
    private async Task RestoreAsync(RestoreSessionViewModel sessionVm)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        var selected = sessionVm.Entries.Where(e => e.Status == IndexDeletionStatus.Dropped || e.Status == IndexDeletionStatus.Pending).ToList();
        var service = new RestoreService();
        var auditPath = Path.Combine(_paths.ConfigDir, "audit.jsonl");
        var result = await service.RestoreAsync(sessionVm.Session, selected, _provider, auditPath, CancellationToken.None).ConfigureAwait(true);
        StatusMessage = $"Restored {result.Restored.Count}, failed {result.Failed.Count}";
        await LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
