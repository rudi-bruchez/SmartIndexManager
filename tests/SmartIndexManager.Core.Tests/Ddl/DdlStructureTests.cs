using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Ddl;

public class DdlStructureTests
{
    private static IndexModel Nc(
        IndexColumn[] keys, string[]? includes = null, string? filter = null, bool unique = false) => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Orders_Legacy",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = keys, IncludedColumns = includes ?? [], FilterPredicate = filter, IsUnique = unique
    };

    private static string Sql(DdlResult r) => Assert.IsType<DdlSuccess>(r).Sql;

    [Fact]
    public void Generates_basic_nonclustered_index()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("CustomerId", SortDirection.Ascending)])));

        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Orders_Legacy]", ddl);
        Assert.Contains("ON [dbo].[Orders] ([CustomerId] ASC)", ddl);
        Assert.EndsWith(";", ddl.TrimEnd());
    }

    [Fact]
    public void Emits_unique_keyword_and_direction()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("OrderDate", SortDirection.Descending)], unique: true)));

        Assert.Contains("CREATE UNIQUE NONCLUSTERED INDEX", ddl);
        Assert.Contains("[OrderDate] DESC", ddl);
    }

    [Fact]
    public void Emits_include_and_filter()
    {
        var ddl = Sql(SqlServerDdlGenerator.Generate(
            Nc([new IndexColumn("CustomerId", SortDirection.Ascending)],
               includes: ["Total", "Qty"], filter: "[Status] = 1")));

        Assert.Contains("INCLUDE ([Total], [Qty])", ddl);
        Assert.Contains("WHERE [Status] = 1", ddl);
    }

    [Fact]
    public void Escapes_closing_bracket_in_identifiers()
    {
        var index = Nc([new IndexColumn("Weird]Name", SortDirection.Ascending)]);
        var ddl = Sql(SqlServerDdlGenerator.Generate(index));
        Assert.Contains("[Weird]]Name] ASC", ddl);
    }
}
