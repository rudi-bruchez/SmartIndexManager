using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR3Tests
{
    private static IndexModel Nc(string name, string[] keys, string[]? includes = null,
        bool unique = false, string? filter = null) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        IncludedColumns = includes ?? [], IsUnique = unique, FilterPredicate = filter
    };

    [Fact]
    public void Same_key_with_strictly_smaller_includes_is_R3()
    {
        var a = Nc("IX_Small", ["CustomerId"], includes: ["Total"]);
        var b = Nc("IX_Big", ["CustomerId"], includes: ["Total", "Qty"]);

        var r3 = Assert.Single(RedundancyAnalyzer.Analyze([a, b]));
        Assert.Equal(RedundancyRule.R3DominatedIncludes, r3.Rule);
        Assert.Equal("IX_Small", r3.Redundant.Name);
        Assert.Equal("IX_Big", r3.CoveredBy.Name);
    }

    [Fact]
    public void Equal_includes_is_R1_not_R3()
    {
        var a = Nc("IX_A", ["CustomerId"], includes: ["Total"]);
        var b = Nc("IX_B", ["CustomerId"], includes: ["Total"]);
        Assert.Equal(RedundancyRule.R1ExactDuplicate, Assert.Single(RedundancyAnalyzer.Analyze([a, b])).Rule);
    }

    [Fact]
    public void Filtered_versus_non_filtered_is_not_redundant()
    {
        var a = Nc("IX_Filtered", ["CustomerId"], filter: "Status = 1");
        var b = Nc("IX_Plain", ["CustomerId"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Partial_key_overlap_is_not_redundant()
    {
        var a = Nc("IX_A", ["CustomerId", "OrderDate"]);
        var b = Nc("IX_B", ["CustomerId", "ShipDate"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
