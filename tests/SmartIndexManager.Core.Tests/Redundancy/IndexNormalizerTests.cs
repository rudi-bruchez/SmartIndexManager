using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class IndexNormalizerTests
{
    private static IndexModel Index(string filter = null!, params string[] keys) => new()
    {
        Database = "db", Schema = "dbo", Table = "T", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        FilterPredicate = filter
    };

    [Fact]
    public void Key_columns_are_lowercased_for_comparison()
    {
        var n = IndexNormalizer.Normalize(Index(null!, "CustomerId", "OrderDate"));
        Assert.Equal(new[] { "customerid", "orderdate" }, n.Key.Select(k => k.Column));
    }

    [Fact]
    public void Includes_are_a_case_insensitive_set()
    {
        var index = Index(null!, "A") with { IncludedColumns = new[] { "Total", "total", "Qty" } };
        var n = IndexNormalizer.Normalize(index);
        Assert.Equal(2, n.Includes.Count);
        Assert.Contains("total", n.Includes);
        Assert.Contains("qty", n.Includes);
    }

    [Theory]
    [InlineData("[Status] = 1", "[status] = 1")]
    [InlineData("(  Status   =   1  )", "status = 1")]
    [InlineData("((Status = 1))", "status = 1")]
    [InlineData(null, null)]
    public void Filter_is_normalized_syntactically(string? input, string? expected)
    {
        Assert.Equal(expected, IndexNormalizer.NormalizeFilter(input));
    }
}
