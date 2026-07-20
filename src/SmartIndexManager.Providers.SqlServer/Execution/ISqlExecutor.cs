using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Execution;

public interface ISqlExecutor : IAsyncDisposable
{
    // Runs a loaded script and validates that every column the header declares is
    // present in the result set (by name); a missing declared column is an invalid file.
    Task<IReadOnlyList<SqlRow>> QueryAsync(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    Task<T?> ScalarAsync<T>(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken);

    // Runs a mutating statement (DROP, ALTER). timeout null uses the connection default.
    Task<int> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout, CancellationToken cancellationToken);
}
