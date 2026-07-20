using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer;

public sealed partial class SqlServerIndexProvider : IIndexProvider
{
    private readonly ISqlExecutor _executor;
    private readonly string _scriptRoot;

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

    public ValueTask DisposeAsync() => _executor.DisposeAsync();

    // GetIndexesAsync, GetQueryUsageAsync, GetHintsAsync, GetQueryStoreStateAsync,
    // EnableQueryStoreAsync and DropIndexAsync are added in Tasks 14-16 (partial class).
}
