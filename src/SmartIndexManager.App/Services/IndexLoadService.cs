using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Redundancy;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Services;

public sealed class IndexLoadService : IIndexLoadService
{
    private readonly IIndexProviderFactory _factory;
    private readonly IAppPaths _paths;
    private readonly ConfidenceScorer _scorer = new();

    public IndexLoadService(IIndexProviderFactory factory, IAppPaths paths)
    {
        _factory = factory;
        _paths = paths;
    }

    public async Task<LoadResult> LoadAsync(
        ConnectionProfile profile, string? password,
        IReadOnlyList<string> databases, CancellationToken cancellationToken)
    {
        var request = ToRequest(profile);
        await using var provider = await _factory.ConnectAsync(request, password, cancellationToken).ConfigureAwait(false);

        var indexes = await provider.GetIndexesAsync(databases, cancellationToken).ConfigureAwait(false);

        WriteSnapshot(provider.ServerInfo, databases, indexes);

        var redundant = new HashSet<(string, string, string, string)>();
        foreach (var f in RedundancyAnalyzer.Analyze(indexes))
        {
            redundant.Add(Key(f.Redundant));
            redundant.Add(Key(f.CoveredBy));
        }

        int uptime = Math.Max(0, provider.ServerInfo.UptimeDays);
        var rows = new List<IndexRowViewModel>(indexes.Count);
        foreach (var index in indexes)
        {
            bool isRedundant = redundant.Contains(Key(index));
            bool fkSupport = index.ProviderProperties.ContainsKey("fkSupport");

            var safety = DeletionSafetyEvaluator.Evaluate(new SafetyInputs
            {
                Index = index,
                Ddl = SqlServerDdlGenerator.Generate(index),
                SupportsForeignKey = fkSupport,
                InstanceUptimeDays = uptime
            });

            ConfidenceScore? score = safety.Eligibility == DeletionEligibility.Deletable
                ? _scorer.Score(new ScoreInputs
                {
                    Index = index,
                    InstanceUptimeDays = uptime,
                    IsRedundant = isRedundant,
                    SupportsForeignKey = fkSupport,
                    NowUtc = DateTime.UtcNow
                })
                : null;

            rows.Add(new IndexRowViewModel(index, score, safety, isRedundant, isReferencedByHint: false));
        }

        return new LoadResult(provider.ServerInfo, provider.Capabilities, provider.Permissions, rows);
    }

    private void WriteSnapshot(ServerInfo server, IReadOnlyList<string> databases, IReadOnlyList<IndexModel> indexes)
    {
        foreach (var database in databases)
        {
            var forDb = indexes.Where(i => string.Equals(i.Database, database, StringComparison.OrdinalIgnoreCase))
                .Select(i => new SnapshotIndexUsage
                {
                    Schema = i.Schema, Table = i.Table, Index = i.Name,
                    Seeks = i.Usage.Seeks, Scans = i.Usage.Scans, Lookups = i.Usage.Lookups,
                    Updates = i.Usage.Updates, LastRead = i.Usage.LastRead
                })
                .ToList();
            var snapshot = new UsageSnapshot
            {
                CapturedUtc = DateTime.UtcNow,
                Server = server.ServerName,
                Database = database,
                InstanceUptimeDays = server.UptimeDays,
                Indexes = forDb
            };
            SnapshotStore.Write(_paths.SnapshotRoot, snapshot);
        }
    }

    private static (string, string, string, string) Key(IndexModel i) => (i.Database, i.Schema, i.Table, i.Name);

    private static ConnectionRequest ToRequest(ConnectionProfile p) => new()
    {
        Server = p.Server, Port = p.Port, Auth = p.Auth, Login = p.Login,
        Encrypt = p.Encrypt, TrustServerCertificate = p.TrustServerCertificate
    };
}
