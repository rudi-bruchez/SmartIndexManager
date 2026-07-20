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
        if (_provider is null)
            throw new InvalidOperationException("Provider must be set before showing detail.");

        var index = row.Index;

        Ddl = SqlServerDdlGenerator.Generate(index) switch
        {
            DdlSuccess s => s.Sql,
            DdlNotBackupable n => $"-- {n.Reason}",
            _ => ""
        };

        var reference = IndexRef.Of(index);
        Queries.Clear();
        foreach (var q in await _provider.GetQueryUsageAsync(reference, cancellationToken))
            Queries.Add(q);

        Hints.Clear();
        foreach (var h in await _provider.GetHintsAsync(reference, cancellationToken))
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
