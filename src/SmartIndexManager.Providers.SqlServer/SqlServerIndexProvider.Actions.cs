using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    // Stubs: real implementations land in Task 16 (overwrites this file).
    public Task EnableQueryStoreAsync(
        string database, QueryStoreSettings settings, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();

    public Task DropIndexAsync(
        IndexRef index, TimeSpan timeout, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
