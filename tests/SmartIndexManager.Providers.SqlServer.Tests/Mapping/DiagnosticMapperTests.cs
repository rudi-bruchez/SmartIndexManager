using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class DiagnosticMapperTests
{
    [Fact]
    public void Usage_mapper_carries_source_and_fields()
    {
        var row = new SqlRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["QueryText"] = "SELECT ...", ["ExecutionCount"] = 42L,
            ["LastExecutionUtc"] = new DateTime(2026, 07, 20, 8, 0, 0, DateTimeKind.Utc)
        });

        var usage = QueryUsageMapper.Map(row, UsageSource.QueryStore);
        Assert.Equal(UsageSource.QueryStore, usage.Source);
        Assert.Equal(42L, usage.ExecutionCount);
        Assert.Equal(2026, usage.LastExecutionUtc!.Value.Year);
    }

    [Fact]
    public void Hint_mapper_reads_reference_and_kind()
    {
        var row = new SqlRow(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reference"] = "dbo.GetOrders", ["Kind"] = "query hint"
        });
        var hint = HintMapper.Map(row);
        Assert.Equal("dbo.GetOrders", hint.Reference);
        Assert.Equal("query hint", hint.Kind);
    }

    [Theory]
    [InlineData(null, QueryStoreState.Off)]
    [InlineData(0, QueryStoreState.Off)]
    [InlineData(1, QueryStoreState.ReadOnly)]
    [InlineData(2, QueryStoreState.ReadWrite)]
    public void Query_store_state_maps_actual_state_code(int? code, QueryStoreState expected)
        => Assert.Equal(expected, QueryStoreStateMapper.Map(code));
}
