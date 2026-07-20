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
