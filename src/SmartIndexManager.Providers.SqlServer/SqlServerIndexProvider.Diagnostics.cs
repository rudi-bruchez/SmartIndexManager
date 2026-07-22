using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        // Plan-cache and Query Store reads need VIEW SERVER STATE / VIEW DATABASE STATE.
        // Without it, degrade gracefully (the PermissionReport already flags this) instead of throwing.
        if (!Permissions.CanViewState) return Task.FromResult<IReadOnlyList<QueryUsage>>([]);

        return ExclusiveAsync(async ct =>
        {
            await UseDatabaseAsync(index.Database, ct).ConfigureAwait(false);
            var parameters = new Dictionary<string, object?> { ["@IndexName"] = index.Index };

            var usage = new List<QueryUsage>();
            if (Capabilities.SupportsPlanCache)
            {
                var rows = await QueryAsync("index-used-by-queries", parameters, ct).ConfigureAwait(false);
                usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.PlanCache)));
            }
            // The database is already current here: read Query Store state without switching again.
            if (Capabilities.SupportsQueryStore
                && await ReadQueryStoreStateAsync(ct).ConfigureAwait(false) != QueryStoreState.Off)
            {
                var rows = await QueryAsync("index-used-by-queries-query-store", parameters, ct).ConfigureAwait(false);
                usage.AddRange(rows.Select(r => QueryUsageMapper.Map(r, UsageSource.QueryStore)));
            }
            return (IReadOnlyList<QueryUsage>)usage;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default)
    {
        if (!Permissions.CanViewState) return Task.FromResult<IReadOnlyList<IndexHint>>([]);

        return ExclusiveAsync(async ct =>
        {
            await UseDatabaseAsync(index.Database, ct).ConfigureAwait(false);
            var rows = await QueryAsync("index-hints-plancache",
                new Dictionary<string, object?> { ["@IndexName"] = index.Index }, ct).ConfigureAwait(false);
            return (IReadOnlyList<IndexHint>)rows.Select(HintMapper.Map).ToList();
        }, cancellationToken);
    }

    public Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsQueryStore) return Task.FromResult(QueryStoreState.NotSupported);
        return ExclusiveAsync(async ct =>
        {
            await UseDatabaseAsync(database, ct).ConfigureAwait(false);
            return await ReadQueryStoreStateAsync(ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    // Reads Query Store state assuming the target database is already current (no re-switch).
    private async Task<QueryStoreState> ReadQueryStoreStateAsync(CancellationToken cancellationToken)
    {
        var script = Core.Sql.SqlScriptLoader.Load(_scriptRoot, "querystore-state");
        var state = await _executor.ScalarAsync<int?>(script, null, cancellationToken).ConfigureAwait(false);
        return QueryStoreStateMapper.Map(state);
    }
}
