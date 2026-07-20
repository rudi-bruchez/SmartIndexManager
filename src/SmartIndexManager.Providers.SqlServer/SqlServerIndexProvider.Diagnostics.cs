using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    // Stubs: real implementations land in Task 15 (overwrites this file).
    public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
