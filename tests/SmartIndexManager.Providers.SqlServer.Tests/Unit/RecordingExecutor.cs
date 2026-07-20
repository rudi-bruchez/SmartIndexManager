using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Tests.Unit;

// Fake ISqlExecutor for gate tests: records what the provider called instead of
// touching a real connection, so the permission/eligibility gates can be verified
// without Docker.
public sealed class RecordingExecutor : ISqlExecutor
{
    public int QueryCount { get; private set; }
    public int ScalarCount { get; private set; }
    public int ExecuteCount { get; private set; }
    public int ChangeDatabaseCount { get; private set; }
    public string? LastExecutedSql { get; private set; }

    public IReadOnlyList<SqlRow> QueryResult { get; set; } = [];
    public bool? ScalarBoolResult { get; set; }

    public Task<IReadOnlyList<SqlRow>> QueryAsync(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        QueryCount++;
        return Task.FromResult(QueryResult);
    }

    public Task<T?> ScalarAsync<T>(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        ScalarCount++;
        if (typeof(T) == typeof(bool?) || typeof(T) == typeof(bool))
            return Task.FromResult((T?)(object?)ScalarBoolResult);
        return Task.FromResult(default(T));
    }

    public Task<int> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ExecuteCount++;
        LastExecutedSql = sql;
        return Task.FromResult(0);
    }

    public Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        ChangeDatabaseCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
