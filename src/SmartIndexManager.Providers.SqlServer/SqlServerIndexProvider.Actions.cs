using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public async Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (!Permissions.CanAlter)
            throw new InvalidOperationException("DROP INDEX requires ALTER permission, which the current login lacks.");
        await UseDatabaseAsync(index.Database, cancellationToken).ConfigureAwait(false);

        // Defence in depth: refuse anything that is not a plain nonclustered rowstore non-unique DROP
        // INDEX target, even though the caller (Core DeletionSafetyEvaluator) should already have gated it.
        var check = SqlScriptLoader.Load(_scriptRoot, "index-droppable-check");
        var droppable = await _executor.ScalarAsync<bool?>(check,
            new Dictionary<string, object?>
            {
                ["@SchemaName"] = index.Schema, ["@TableName"] = index.Table, ["@IndexName"] = index.Index
            },
            cancellationToken).ConfigureAwait(false);
        if (droppable != true)
            throw new InvalidOperationException(
                $"Index {index.Schema}.{index.Table}.{index.Index} is not a droppable nonclustered rowstore non-unique index; refusing DROP.");

        // DROP INDEX <index> ON <schema>.<table>. Identifiers cannot be parameterized, so they are
        // bracket-quoted (]] escaped). The caller (Core DeletionSafetyEvaluator) has already gated eligibility.
        var sql = $"DROP INDEX {Quote(index.Index)} ON {Quote(index.Schema)}.{Quote(index.Table)};";
        await _executor.ExecuteAsync(sql, null, timeout, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnableQueryStoreAsync(
        string database, QueryStoreSettings settings, CancellationToken cancellationToken = default)
    {
        if (!Capabilities.SupportsQueryStore)
            throw new InvalidOperationException("Query Store is not supported on this server.");
        if (!Permissions.CanAlter)
            throw new InvalidOperationException("Enabling Query Store requires ALTER permission, which the current login lacks.");
        await UseDatabaseAsync(database, cancellationToken).ConfigureAwait(false);

        var script = SqlScriptLoader.Load(_scriptRoot, "querystore-enable");
        var parameters = new Dictionary<string, object?>
        {
            ["@DatabaseName"] = database,
            ["@MaxStorageSizeMb"] = settings.MaxStorageSizeMb,
            ["@StaleQueryThresholdDays"] = settings.StaleQueryThresholdDays
        };
        await _executor.ScalarAsync<bool?>(script, parameters, cancellationToken).ConfigureAwait(false);
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
