using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR2Tests
{
    private static IndexModel Nc(string name, (string, SortDirection)[] keys,
        string[]? includes = null, bool unique = false, string? filter = null) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k.Item1, k.Item2)).ToList(),
        IncludedColumns = includes ?? [], IsUnique = unique, FilterPredicate = filter
    };

    private static (string, SortDirection) Asc(string c) => (c, SortDirection.Ascending);
    private static (string, SortDirection) Desc(string c) => (c, SortDirection.Descending);

    [Fact]
    public void Strict_prefix_with_included_covered_is_R2()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r2 = Assert.Single(findings);
        Assert.Equal(RedundancyRule.R2CoveredPrefix, r2.Rule);
        Assert.Equal("IX_Cust", r2.Redundant.Name);
        Assert.Equal("IX_CustDate", r2.CoveredBy.Name);
    }

    [Fact]
    public void Prefix_includes_must_be_covered_by_key_or_includes_of_the_longer()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")], includes: ["Total"]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")], includes: ["Total"]);
        Assert.Equal(RedundancyRule.R2CoveredPrefix, Assert.Single(RedundancyAnalyzer.Analyze([a, b])).Rule);

        var c = Nc("IX_Cust2", [Asc("CustomerId")], includes: ["Qty"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([c, b])); // Qty not covered by b
    }

    [Fact]
    public void Different_direction_on_prefix_breaks_R2()
    {
        var a = Nc("IX_Cust", [Desc("CustomerId")]);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Different_filter_breaks_R2()
    {
        var a = Nc("IX_Cust", [Asc("CustomerId")], filter: "Status = 1");
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")], filter: "Status = 2");
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Unique_shorter_index_is_never_flagged()
    {
        var a = Nc("UQ_Cust", [Asc("CustomerId")], unique: true);
        var b = Nc("IX_CustDate", [Asc("CustomerId"), Asc("OrderDate")]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
