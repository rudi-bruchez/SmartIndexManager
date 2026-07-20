using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class IndexDetailViewModel : ViewModelBase
{
    private readonly IIndexProvider _provider;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    [ObservableProperty] private string _ddl = "";
    [ObservableProperty] private string _oldestSnapshotText = "";

    public ObservableCollection<QueryUsage> Queries { get; } = [];
    public ObservableCollection<IndexHint> Hints { get; } = [];
    public ObservableCollection<ScoreFactor> ScoreFactors { get; } = [];

    public IndexDetailViewModel(IIndexProvider provider, IAppPaths paths, ILocalizer loc)
    {
        _provider = provider;
        _paths = paths;
        _loc = loc;
    }

    public async Task ShowAsync(IndexRowViewModel row, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var index = row.Index;

        Ddl = SqlServerDdlGenerator.Generate(index) switch
        {
            DdlSuccess s => s.Sql,
            DdlNotBackupable n => $"-- {n.Reason}",
            _ => ""
        };

        var reference = IndexRef.Of(index);
        var queries = await _provider.GetQueryUsageAsync(reference, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Queries.Clear();
        foreach (var q in queries)
            Queries.Add(q);

        var hints = await _provider.GetHintsAsync(reference, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Hints.Clear();
        foreach (var h in hints)
            Hints.Add(h);

        ScoreFactors.Clear();
        foreach (var f in row.ScoreDetail?.Factors ?? [])
            ScoreFactors.Add(f);

        var oldest = SnapshotStore.OldestCaptureUtc(_paths.SnapshotRoot, _provider.ServerInfo.ServerName, index.Database);
        OldestSnapshotText = oldest is DateTime d
            ? string.Format(_loc["Detail_OldestSnapshot"], d.ToString("yyyy-MM-dd"))
            : "";
    }
}
