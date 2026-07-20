using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class IndexColumnMapperTests
{
    private static SqlRow Row(string name, int ordinal, bool included, bool desc) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ObjectId"] = 1L, ["IndexId"] = 2, ["ColumnName"] = name,
        ["KeyOrdinal"] = ordinal, ["IsIncluded"] = included, ["IsDescending"] = desc
    });

    [Fact]
    public void Key_columns_are_ordered_by_ordinal_with_direction()
    {
        var rows = new[]
        {
            IndexColumnMapper.Map(Row("OrderDate", 2, false, true)),
            IndexColumnMapper.Map(Row("CustomerId", 1, false, false))
        };

        var keys = IndexColumnMapper.KeyColumns(rows);
        Assert.Equal(new[] { "CustomerId", "OrderDate" }, keys.Select(k => k.Name));
        Assert.Equal(SortDirection.Ascending, keys[0].Direction);
        Assert.Equal(SortDirection.Descending, keys[1].Direction);
    }

    [Fact]
    public void Included_columns_are_separated_from_key_columns()
    {
        var rows = new[]
        {
            IndexColumnMapper.Map(Row("CustomerId", 1, false, false)),
            IndexColumnMapper.Map(Row("Total", 0, true, false))
        };

        Assert.Equal(new[] { "CustomerId" }, IndexColumnMapper.KeyColumns(rows).Select(k => k.Name));
        Assert.Equal(new[] { "Total" }, IndexColumnMapper.IncludedColumns(rows));
    }
}
