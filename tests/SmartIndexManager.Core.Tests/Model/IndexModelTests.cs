using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Model;

public class IndexModelTests
{
    [Fact]
    public void TotalReads_sums_seeks_scans_lookups()
    {
        var stats = new IndexUsageStats(3, 5, 2, 10, null, null);
        Assert.Equal(10, stats.TotalReads);
    }

    [Fact]
    public void IndexModel_defaults_are_empty_not_null()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders",
            Name = "IX_Orders", Type = IndexType.NonclusteredRowstore
        };

        Assert.Empty(index.KeyColumns);
        Assert.Empty(index.IncludedColumns);
        Assert.Equal(ConstraintKind.None, index.Constraint);
        Assert.Same(IndexUsageStats.Empty, index.Usage);
    }
}
