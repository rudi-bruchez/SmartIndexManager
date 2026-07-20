using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Capabilities;
using SmartIndexManager.Providers.SqlServer.Connection;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer;

public sealed class SqlServerIndexProviderFactory : IIndexProviderFactory
{
    private readonly string _scriptRoot;

    public SqlServerIndexProviderFactory(string scriptRoot) => _scriptRoot = scriptRoot;

    public async Task<IIndexProvider> ConnectAsync(
        ConnectionRequest request, string? password, CancellationToken cancellationToken = default)
    {
        var connectionString = SqlServerConnectionFactory.BuildConnectionString(request, password);
        var connection = new SqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

            var executor = new SqlClientExecutor(connection);
            var serverInfo = await DetectServerInfoAsync(executor, cancellationToken).ConfigureAwait(false);
            var permissions = await DetectPermissionsAsync(executor, cancellationToken).ConfigureAwait(false);
            var capabilities = CapabilityResolver.Resolve(serverInfo);

            // Ownership of the connection passes to the provider (disposed via the executor).
            return new SqlServerIndexProvider(executor, _scriptRoot, serverInfo, capabilities, permissions);
        }
        catch
        {
            // Open succeeded but detection failed: do not leak the connection.
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ServerInfo> DetectServerInfoAsync(ISqlExecutor executor, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, "server-info");
        var rows = await executor.QueryAsync(script, null, ct).ConfigureAwait(false);
        return ServerInfoMapper.Map(rows[0]);
    }

    private async Task<PermissionReport> DetectPermissionsAsync(ISqlExecutor executor, CancellationToken ct)
    {
        var script = SqlScriptLoader.Load(_scriptRoot, "permissions-check");
        var rows = await executor.QueryAsync(script, null, ct).ConfigureAwait(false);
        return PermissionMapper.Map(rows[0]);
    }
}
