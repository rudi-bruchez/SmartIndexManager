using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Redundancy;
using Xunit;

namespace SmartIndexManager.Core.Tests.Redundancy;

public class RedundancyR1Tests
{
    private static IndexModel Nc(string name, string[] keys, string[]? includes = null,
        bool unique = false, ConstraintKind constraint = ConstraintKind.None,
        long reads = 0) => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = name,
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys.Select(k => new IndexColumn(k, SortDirection.Ascending)).ToList(),
        IncludedColumns = includes ?? [],
        IsUnique = unique, Constraint = constraint,
        Usage = new IndexUsageStats(reads, 0, 0, 0, null, null)
    };

    [Fact]
    public void Identical_indexes_produce_one_R1_finding()
    {
        var a = Nc("IX_A", ["CustomerId"], reads: 100);
        var b = Nc("IX_B", ["CustomerId"], reads: 0);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r1 = Assert.Single(findings);
        Assert.Equal(RedundancyRule.R1ExactDuplicate, r1.Rule);
        Assert.Equal("IX_B", r1.Redundant.Name);   // keep the more-used one
        Assert.Equal("IX_A", r1.CoveredBy.Name);
    }

    [Fact]
    public void Duplicate_keeps_the_constraint_backed_index()
    {
        var a = Nc("IX_Plain", ["CustomerId"], reads: 0);
        var b = Nc("UQ_Cust", ["CustomerId"], unique: true, constraint: ConstraintKind.Unique);

        var findings = RedundancyAnalyzer.Analyze([a, b]);

        var r1 = Assert.Single(findings);
        Assert.Equal("IX_Plain", r1.Redundant.Name);
        Assert.Equal("UQ_Cust", r1.CoveredBy.Name);
    }

    [Fact]
    public void Unique_index_is_never_the_redundant_one()
    {
        var a = Nc("UQ_A", ["CustomerId"], unique: true);
        var b = Nc("UQ_B", ["CustomerId"], unique: true);

        // both unique => neither may be flagged redundant
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Different_tables_are_never_compared()
    {
        var a = Nc("IX_A", ["CustomerId"]);
        var b = Nc("IX_B", ["CustomerId"]) with { Table = "Customers" };
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }

    [Fact]
    public void Disabled_indexes_are_excluded_from_analysis()
    {
        var a = Nc("IX_A", ["CustomerId"]) with { IsDisabled = true };
        var b = Nc("IX_B", ["CustomerId"]);
        Assert.Empty(RedundancyAnalyzer.Analyze([a, b]));
    }
}
