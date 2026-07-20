using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider
{
    // Stub: real implementation lands in Task 14 (overwrites this file).
    public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(
        IReadOnlyList<string> databases, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
