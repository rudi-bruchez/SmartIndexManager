using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ActionsTests
{
    private readonly SqlServerContainerFixture _fx;
    public ActionsTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Drop_index_removes_it_from_the_listing()
    {
        // Seed a disposable index so the shared fixture stays intact.
        await using (var conn = new SqlConnection(_fx.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "IF INDEXPROPERTY(OBJECT_ID('dbo.Orders'),'IX_Tmp_Drop','IndexID') IS NULL CREATE INDEX IX_Tmp_Drop ON dbo.Orders(Total);";
            await cmd.ExecuteNonQueryAsync();
        }

        await using var provider = await _fx.ConnectAsync();
        await provider.DropIndexAsync(
            new IndexRef(_fx.Database, "dbo", "Orders", "IX_Tmp_Drop"), TimeSpan.FromSeconds(30), CancellationToken.None);

        var remaining = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);
        Assert.DoesNotContain(remaining, i => i.Name == "IX_Tmp_Drop");
    }

    [RequiresDockerFact]
    public async Task Enable_query_store_moves_state_to_read_write()
    {
        await using var provider = await _fx.ConnectAsync();

        await provider.EnableQueryStoreAsync(_fx.Database, new QueryStoreSettings(), CancellationToken.None);
        var state = await provider.GetQueryStoreStateAsync(_fx.Database, CancellationToken.None);

        Assert.Equal(QueryStoreState.ReadWrite, state);
    }
}
