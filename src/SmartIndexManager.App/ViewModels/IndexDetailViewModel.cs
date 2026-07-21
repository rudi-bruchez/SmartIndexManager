using System.Collections.ObjectModel;
using System.Linq;
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
    [ObservableProperty] private string _headerName = "";
    [ObservableProperty] private string _typeText = "";
    [ObservableProperty] private string _keyColumnsText = "";
    [ObservableProperty] private string _includesText = "";
    [ObservableProperty] private bool _isUnique;
    [ObservableProperty] private bool _isRedundant;
    [ObservableProperty] private int? _score;
    [ObservableProperty] private bool _isScoreSafe;
    [ObservableProperty] private bool _isScoreCaution;
    [ObservableProperty] private bool _isScoreRisk;

    public ObservableCollection<QueryUsage> Queries { get; } = [];
    public ObservableCollection<IndexHint> Hints { get; } = [];
    public ObservableCollection<ScoreFactor> ScoreFactors { get; } = [];
    public ObservableCollection<KeyValuePair<string, string>> ProviderProps { get; } = [];

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

        // Populate the header/structure/score cards from the selected row (no provider round-trip).
        HeaderName = index.Name;
        TypeText = index.Type.ToString();
        KeyColumnsText = string.Join(", ", index.KeyColumns.Select(c => c.Name));
        IncludesText = string.Join(", ", index.IncludedColumns);
        IsUnique = index.IsUnique;
        IsRedundant = row.Redundant;
        Score = row.Score;
        IsScoreSafe = row.IsScoreSafe;
        IsScoreCaution = row.IsScoreCaution;
        IsScoreRisk = row.IsScoreRisk;
        ProviderProps.Clear();
        foreach (var kv in index.ProviderProperties)
            ProviderProps.Add(new KeyValuePair<string, string>(kv.Key, kv.Value?.ToString() ?? ""));

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
