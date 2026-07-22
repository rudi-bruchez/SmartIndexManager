using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public sealed class FakeIndexProvider : IIndexProvider
{
    public required ServerInfo ServerInfo { get; init; }
    public required ProviderCapabilities Capabilities { get; init; }
    public required PermissionReport Permissions { get; init; }

    public IReadOnlyList<IndexModel> Indexes { get; init; } = [];
    public IReadOnlyList<QueryUsage> Usage { get; init; } = [];
    public IReadOnlyList<IndexHint> Hints { get; init; } = [];
    public QueryStoreState QueryStore { get; init; } = QueryStoreState.Off;
    public Exception? QueryUsageException { get; set; }
    public bool Disposed { get; private set; }

    public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct = default)
        => Task.FromResult(Indexes);

    public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
        => QueryUsageException is not null
            ? Task.FromException<IReadOnlyList<QueryUsage>>(QueryUsageException)
            : Task.FromResult(Usage);

    public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
        => Task.FromResult(Hints);

    public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
        => Task.FromResult(QueryStore);

    public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
        => Task.CompletedTask;

    public string? LastDdlDatabase { get; private set; }
    public string? LastDdlSql { get; private set; }

    public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct = default)
    {
        LastDdlDatabase = database;
        LastDdlSql = sql;
        return Task.CompletedTask;
    }

    public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> TableExistsAsync(string database, string schema, string table, CancellationToken ct = default)
        => Task.FromResult(true);

    public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
}
