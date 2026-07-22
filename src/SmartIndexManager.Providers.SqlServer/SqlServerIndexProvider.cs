using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider : IIndexProvider
{
    private readonly ISqlExecutor _executor;
    private readonly string _scriptRoot;

    // The provider owns a single connection without MARS, so overlapping commands would
    // throw and interleaved ChangeDatabase calls would corrupt the active-database context.
    // Every public operation runs its whole ChangeDatabase+command sequence under this gate,
    // so callers (browse detail loads, dry-run, deletion, restore) never need to coordinate.
    private readonly SemaphoreSlim _connectionGate = new(1, 1);

    public ServerInfo ServerInfo { get; }
    public ProviderCapabilities Capabilities { get; }
    public PermissionReport Permissions { get; }

    internal SqlServerIndexProvider(
        ISqlExecutor executor, string scriptRoot,
        ServerInfo serverInfo, ProviderCapabilities capabilities, PermissionReport permissions)
    {
        _executor = executor;
        _scriptRoot = scriptRoot;
        ServerInfo = serverInfo;
        Capabilities = capabilities;
        Permissions = permissions;
    }

    // Runs a whole provider operation exclusively on the single connection. Capability and
    // permission guards that do not touch the connection belong before the call, not inside.
    private async Task<T> ExclusiveAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken ct)
    {
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try { return await body(ct).ConfigureAwait(false); }
        finally { _connectionGate.Release(); }
    }

    private async Task ExclusiveAsync(Func<CancellationToken, Task> body, CancellationToken ct)
    {
        await _connectionGate.WaitAsync(ct).ConfigureAwait(false);
        try { await body(ct).ConfigureAwait(false); }
        finally { _connectionGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await _executor.DisposeAsync().ConfigureAwait(false);
        _connectionGate.Dispose();
    }

    // GetIndexesAsync, GetQueryUsageAsync, GetHintsAsync, GetQueryStoreStateAsync,
    // EnableQueryStoreAsync and DropIndexAsync are added in Tasks 14-16 (partial class).
}
