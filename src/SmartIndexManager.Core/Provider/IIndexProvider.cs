using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Provider;

// One connected session against one instance. Created by IIndexProviderFactory.
// All long operations are async and honour the CancellationToken.
public interface IIndexProvider : IAsyncDisposable
{
    ServerInfo ServerInfo { get; }
    ProviderCapabilities Capabilities { get; }
    PermissionReport Permissions { get; }

    Task<IReadOnlyList<IndexModel>> GetIndexesAsync(
        IReadOnlyList<string> databases, CancellationToken cancellationToken = default);

    /// <remarks>Implementations return an empty list when the current login lacks the required
    /// state-viewing permission (VIEW SERVER STATE on-premises, VIEW DATABASE STATE on Azure);
    /// callers see the gap via <see cref="PermissionReport"/> rather than an exception.</remarks>
    Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(
        IndexRef index, CancellationToken cancellationToken = default);

    // Per-index so the server filters by name; the dry-run flags a specific index as
    // hint-referenced. Scanning all hints for a database is never needed.
    /// <remarks>Implementations return an empty list when the current login lacks the required
    /// state-viewing permission (VIEW SERVER STATE on-premises, VIEW DATABASE STATE on Azure);
    /// callers see the gap via <see cref="PermissionReport"/> rather than an exception.</remarks>
    Task<IReadOnlyList<IndexHint>> GetHintsAsync(
        IndexRef index, CancellationToken cancellationToken = default);

    Task<QueryStoreState> GetQueryStoreStateAsync(
        string database, CancellationToken cancellationToken = default);

    Task EnableQueryStoreAsync(
        string database, QueryStoreSettings settings, CancellationToken cancellationToken = default);

    Task DropIndexAsync(
        IndexRef index, TimeSpan timeout, CancellationToken cancellationToken = default);

    Task ExecuteDdlAsync(
        string database, string sql, CancellationToken cancellationToken = default);

    Task<bool> IndexExistsAsync(
        string database, string schema, string table, string index, CancellationToken cancellationToken = default);

    Task<bool> TableExistsAsync(
        string database, string schema, string table, CancellationToken cancellationToken = default);
}
