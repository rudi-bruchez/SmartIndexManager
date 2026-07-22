using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(
        IReadOnlyList<string> databases, CancellationToken cancellationToken = default)
        => ExclusiveAsync(async ct =>
        {
            var result = new List<IndexModel>();
            foreach (var database in databases)
            {
                ct.ThrowIfCancellationRequested();
                result.AddRange(await GetIndexesForDatabaseAsync(database, ct).ConfigureAwait(false));
            }
            return (IReadOnlyList<IndexModel>)result;
        }, cancellationToken);

    private async Task<IReadOnlyList<IndexModel>> GetIndexesForDatabaseAsync(string database, CancellationToken ct)
    {
        // index-list / index-columns / fk-support are database-scoped; run them in the target database.
        await UseDatabaseAsync(database, ct).ConfigureAwait(false);

        var indexRows = await QueryAsync("index-list", null, ct).ConfigureAwait(false);
        var columnRows = await QueryAsync("index-columns", null, ct).ConfigureAwait(false);
        var fkRows = await QueryAsync("fk-support", null, ct).ConfigureAwait(false);

        var columnsByIndex = columnRows
            .Select(IndexColumnMapper.Map)
            .GroupBy(c => (c.ObjectId, c.IndexId))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<IndexColumnRow>)g.ToList());

        var fkSupport = fkRows
            .Select(r => (r.Get<long>("ObjectId"), r.Get<int>("IndexId")))
            .ToHashSet();

        var models = new List<IndexModel>(indexRows.Count);
        foreach (var row in indexRows)
        {
            var key = (row.Get<long>("ObjectId"), row.Get<int>("IndexId"));
            var columns = columnsByIndex.TryGetValue(key, out var c) ? c : [];
            var model = IndexRowMapper.Map(row, columns);
            if (fkSupport.Contains(key))
                model = model with
                {
                    ProviderProperties = new Dictionary<string, string>(model.ProviderProperties) { ["fkSupport"] = "true" }
                };
            models.Add(model);
        }
        return models;
    }

    private async Task<IReadOnlyList<Execution.SqlRow>> QueryAsync(
        string scriptName, IReadOnlyDictionary<string, object?>? parameters, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, scriptName);
        return await _executor.QueryAsync(script, parameters, ct).ConfigureAwait(false);
    }

    // Switching database: SqlConnection.ChangeDatabase actually moves the session's
    // default database (unlike USE inside EXEC, which is scoped to its own batch and
    // does not affect the outer session). The executor validates the name against
    // sys.databases before calling the driver's ChangeDatabase (no injection surface).
    private Task UseDatabaseAsync(string database, CancellationToken ct)
        => _executor.ChangeDatabaseAsync(database, ct);
}
