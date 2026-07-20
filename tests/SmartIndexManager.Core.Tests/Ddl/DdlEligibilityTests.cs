using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Ddl;

public class DdlEligibilityTests
{
    private static IndexModel Base() => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
    };

    [Fact]
    public void Emits_with_options_when_present()
    {
        var index = Base() with
        {
            Options = new IndexOptions
            {
                FillFactor = 80, PadIndex = true, AllowRowLocks = false,
                AllowPageLocks = true, IgnoreDupKey = false, Compression = DataCompression.Page
            }
        };

        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(index)).Sql;

        Assert.Contains("WITH (", ddl);
        Assert.Contains("PAD_INDEX = ON", ddl);
        Assert.Contains("FILLFACTOR = 80", ddl);
        Assert.Contains("ALLOW_ROW_LOCKS = OFF", ddl);
        Assert.Contains("ALLOW_PAGE_LOCKS = ON", ddl);
        Assert.Contains("DATA_COMPRESSION = PAGE", ddl);
        Assert.Contains("IGNORE_DUP_KEY = OFF", ddl);
    }

    [Fact]
    public void Omits_fillfactor_when_not_set()
    {
        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(Base())).Sql;
        Assert.DoesNotContain("FILLFACTOR", ddl);
    }

    [Fact]
    public void Emits_on_filegroup_when_dataspace_is_set()
    {
        var ddl = Assert.IsType<DdlSuccess>(
            SqlServerDdlGenerator.Generate(Base() with { DataSpace = "FG_Archive" })).Sql;
        Assert.Contains("ON [FG_Archive]", ddl);
    }

    [Fact]
    public void Omits_on_clause_on_default_filegroup()
    {
        var ddl = Assert.IsType<DdlSuccess>(SqlServerDdlGenerator.Generate(Base())).Sql;
        // Base() has no DataSpace; only the ON <schema>.<table> target clause is present
        Assert.Equal(1, Occurrences(ddl, " ON "));
    }

    private static int Occurrences(string haystack, string needle)
    {
        int count = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { count++; i += needle.Length; }
        return count;
    }

    [Fact]
    public void Partitioned_index_is_not_backupable()
    {
        var result = SqlServerDdlGenerator.Generate(Base() with { IsPartitioned = true });
        var reason = Assert.IsType<DdlNotBackupable>(result).Reason;
        Assert.Contains("partition", reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_compression_is_not_backupable()
    {
        var index = Base() with { Options = new IndexOptions { Compression = DataCompression.Unsupported } };
        Assert.IsType<DdlNotBackupable>(SqlServerDdlGenerator.Generate(index));
    }

    [Fact]
    public void Non_nonclustered_rowstore_is_not_backupable()
    {
        var index = Base() with { Type = IndexType.Xml };
        Assert.IsType<DdlNotBackupable>(SqlServerDdlGenerator.Generate(index));
    }

    [Fact]
    public void Empty_key_columns_is_not_backupable()
    {
        var index = Base() with { KeyColumns = [] };
        var result = SqlServerDdlGenerator.Generate(index);
        var reason = Assert.IsType<DdlNotBackupable>(result).Reason;
        Assert.Contains("no key columns", reason, System.StringComparison.OrdinalIgnoreCase);
    }
}
