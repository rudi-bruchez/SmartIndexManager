using SmartIndexManager.Providers.SqlServer.Execution;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Execution;

public class SqlRowTests
{
    private static SqlRow Row(params (string, object?)[] cells)
        => new(cells.ToDictionary(c => c.Item1, c => c.Item2, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Get_reads_by_case_insensitive_name()
    {
        var row = Row(("Name", "IX_A"), ("Seeks", 5L));
        Assert.Equal("IX_A", row.Get<string>("name"));
        Assert.Equal(5L, row.Get<long>("SEEKS"));
    }

    [Fact]
    public void Get_maps_DBNull_and_missing_to_default()
    {
        var row = Row(("LastRead", DBNull.Value));
        Assert.Null(row.Get<DateTime?>("LastRead"));
        Assert.Equal(0L, row.Get<long>("Absent"));
    }

    [Fact]
    public void Get_bool_reads_bit_and_int()
    {
        Assert.True(Row(("IsUnique", true)).Get<bool>("IsUnique"));
        Assert.True(Row(("IsUnique", 1)).Get<bool>("IsUnique"));
        Assert.False(Row(("IsUnique", 0)).Get<bool>("IsUnique"));
    }
}
