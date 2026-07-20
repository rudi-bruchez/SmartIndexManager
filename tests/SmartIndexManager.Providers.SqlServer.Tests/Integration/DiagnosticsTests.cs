using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class DiagnosticsTests
{
    private readonly SqlServerContainerFixture _fx;
    public DiagnosticsTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Query_store_state_is_off_on_a_fresh_database()
    {
        await using var provider = await _fx.ConnectAsync();
        var state = await provider.GetQueryStoreStateAsync(_fx.Database, CancellationToken.None);
        Assert.Equal(QueryStoreState.Off, state);
    }

    [RequiresDockerFact]
    public async Task Query_usage_runs_without_error_and_returns_a_list()
    {
        await using var provider = await _fx.ConnectAsync();
        var usage = await provider.GetQueryUsageAsync(
            new IndexRef(_fx.Database, "dbo", "Orders", "IX_Orders_Unused"), CancellationToken.None);
        Assert.NotNull(usage);   // may be empty; the point is the plan-cache query executes
    }
}
