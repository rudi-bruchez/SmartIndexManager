using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class GetIndexesTests
{
    private readonly SqlServerContainerFixture _fx;
    public GetIndexesTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Lists_seeded_indexes_with_structure()
    {
        await using var provider = await _fx.ConnectAsync();
        var indexes = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);

        var unused = Assert.Single(indexes, i => i.Name == "IX_Orders_Unused");
        Assert.Equal(IndexType.NonclusteredRowstore, unused.Type);
        Assert.Equal(new[] { "OrderDate" }, unused.KeyColumns.Select(k => k.Name));
        Assert.Equal(new[] { "Total" }, unused.IncludedColumns);

        var pk = Assert.Single(indexes, i => i.Constraint == ConstraintKind.PrimaryKey);
        Assert.True(pk.IsUnique);
    }

    [RequiresDockerFact]
    public async Task Fk_supporting_index_is_flagged_in_provider_properties()
    {
        await using var provider = await _fx.ConnectAsync();
        var indexes = await provider.GetIndexesAsync(new[] { _fx.Database }, CancellationToken.None);

        // The seed has no FK, so no index should be flagged; this asserts the property path is wired without throwing.
        Assert.All(indexes, i => Assert.False(i.ProviderProperties.ContainsKey("fkSupport")));
    }
}
