using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class DeletionBasketViewModel : ViewModelBase
{
    private readonly DeletionBasket _basket;
    private readonly DeletionOrchestrator _orchestrator;
    private readonly DryRunViewModel _dryRun;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;
    private IIndexProvider? _provider;

    public ObservableCollection<DeletionBasketEntryViewModel> Entries { get; } = [];

    [ObservableProperty] private bool _isConfirmed;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private DryRunViewModel? _activeDryRun;

    public DeletionBasketViewModel(DeletionBasket basket, DeletionOrchestrator orchestrator, DryRunViewModel dryRun, IAppPaths paths, ILocalizer loc)
    {
        _basket = basket;
        _orchestrator = orchestrator;
        _dryRun = dryRun;
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider)
    {
        _provider = provider;
        _dryRun.SetProvider(provider);
    }

    [RelayCommand]
    private async Task RunDryRunAsync(CancellationToken cancellationToken)
    {
        if (_provider is null || _basket.Entries.Count == 0) return;
        IsBusy = true;
        try
        {
            await _dryRun.LoadAsync(cancellationToken).ConfigureAwait(true);
            ActiveDryRun = _dryRun;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(CancellationToken cancellationToken)
    {
        if (!IsConfirmed) return;
        await ExecuteDeletionAsync(DeletionMode.Execute, cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task GenerateScriptAsync(CancellationToken cancellationToken)
    {
        await ExecuteDeletionAsync(DeletionMode.Script, cancellationToken).ConfigureAwait(true);
    }

    private async Task ExecuteDeletionAsync(DeletionMode mode, CancellationToken cancellationToken)
    {
        if (_provider is null || _basket.Entries.Count == 0) return;
        IsBusy = true;
        StatusMessage = _loc[mode == DeletionMode.Execute ? "Action_Delete" : "Action_GenerateScript"];
        try
        {
            var session = new DeletionSession(
                _provider.ServerInfo.ServerName,
                Environment.UserName,
                "1.0.0",
                Math.Max(0, _provider.ServerInfo.UptimeDays),
                _paths.DefaultBackupRoot,
                mode);
            var result = await _orchestrator.DeleteAsync(
                _provider, session, _basket, new DeletionOptions(TimeSpan.FromSeconds(60)), null, cancellationToken).ConfigureAwait(true);
            StatusMessage = $"{mode}: {result.Results.Count(r => r.Status == (mode == DeletionMode.Execute ? IndexDeletionStatus.Dropped : IndexDeletionStatus.Scripted))} / {result.Results.Count}";
            Clear();
        }
        catch (Exception ex)
        {
            // The in-progress message must never be left stuck: surface the failure instead.
            StatusMessage = string.Format(_loc["Action_Failed"], ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Remove(DeletionBasketEntryViewModel entry)
    {
        _basket.Remove(entry.Index);
        Refresh();
    }

    [RelayCommand]
    public void Clear()
    {
        _basket.Clear();
        Refresh();
        ActiveDryRun = null;
    }

    public void Add(IndexModel index, SafetyAssessment safety, ConfidenceScore? score)
    {
        _basket.Add(index, safety, score);
        Refresh();
    }

    private void Refresh()
    {
        Entries.Clear();
        foreach (var e in _basket.Entries)
            Entries.Add(new DeletionBasketEntryViewModel(e, _loc));
    }
}

public sealed class DeletionBasketEntryViewModel
{
    public IndexModel Index { get; }
    public string DisplayName { get; }
    public string Warnings { get; }

    public DeletionBasketEntryViewModel(DeletionBasketEntry entry, ILocalizer loc)
    {
        Index = entry.Index;
        DisplayName = $"{entry.Index.Database}.{entry.Index.Schema}.{entry.Index.Table}.{entry.Index.Name}";
        Warnings = string.Join("; ", entry.Safety.Warnings.Select(w => w.Message));
    }
}
