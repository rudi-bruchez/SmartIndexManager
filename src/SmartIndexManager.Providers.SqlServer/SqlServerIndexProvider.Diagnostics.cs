using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public async Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);
        var parameters = new Dictionary<string, object?> { ["@IndexName"] = index.Index };

        var usage = new List<QueryUsage>();
        if (Capabilities.SupportsPlanCache)
        {
            var rows = await QueryAsync("index-used-by-queries", parameters, cancellationToken).ConfigureAwait(false);
            usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.PlanCache)));
        }
        // The database is already current here: read Query Store state without switching again.
        if (Capabilities.SupportsQueryStore
            && await ReadQueryStoreStateAsync(cancellationToken).ConfigureAwait(false) != QueryStoreState.Off)
        {
            var rows = await QueryAsync("index-used-by-queries-query-store", parameters, cancellationToken).ConfigureAwait(false);
            usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.QueryStore)));
        }
        return usage;
    }

    public async Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);
        var rows = await QueryAsync("index-hints-plancache",
            new Dictionary<string, object?> { ["@IndexName"] = index.Index }, cancellationToken).ConfigureAwait(false);
        return rows.Select(HintMapper.Map).ToList();
    }

    public async Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsQueryStore) return QueryStoreState.NotSupported;
        await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
        return await ReadQueryStoreStateAsync(cancellationToken).ConfigureAwait(false);
    }

    // Reads Query Store state assuming the target database is already current (no re-switch).
    private async Task<QueryStoreState> ReadQueryStoreStateAsync(CancellationToken cancellationToken)
    {
        var script = Core.Sql.SqlScriptLoader.Load(_scriptRoot, "querystore-state");
        var state = await _executor.ScalarAsync<int?>(script, null, cancellationToken).ConfigureAwait(false);
        return QueryStoreStateMapper.Map(state);
    }
}
