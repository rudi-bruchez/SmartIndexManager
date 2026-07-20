using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ExecutorIntegrationTests
{
    private readonly SqlServerContainerFixture _fx;
    public ExecutorIntegrationTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Runs_index_list_and_returns_seeded_indexes()
    {
        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var executor = new SqlClientExecutor(conn);

        var script = SqlScriptLoader.Load(SqlServerContainerFixture.ScriptRoot(), "index-list");
        var rows = await executor.QueryAsync(script, null, CancellationToken.None);

        var names = rows.Select(r => r.Get<string>("IndexName")).ToList();
        Assert.Contains("IX_Orders_Customer", names);
        Assert.Contains("IX_Orders_Unused", names);
    }

    [RequiresDockerFact]
    public async Task Missing_declared_column_throws()
    {
        await using var conn = new SqlConnection(_fx.ConnectionString);
        await conn.OpenAsync();
        await using var executor = new SqlClientExecutor(conn);

        // A script whose header declares a column the query does not return.
        var bad = new SqlScript("bad", "SELECT 1 AS Present;",
            new SqlFileHeader("bad", new Version(11, 0), AzureSupport.Supported, new[] { "Missing" }));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.QueryAsync(bad, null, CancellationToken.None));
    }
}
