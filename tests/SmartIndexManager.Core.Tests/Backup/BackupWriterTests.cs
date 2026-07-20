using SmartIndexManager.Core.Backup;
using SmartIndexManager.Core.Model;
using Xunit;

namespace SmartIndexManager.Core.Tests.Backup;

public class BackupWriterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-backup-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Writes_ddl_with_a_comment_header_and_returns_the_file_name()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Legacy",
            Type = IndexType.NonclusteredRowstore
        };
        var header = new BackupHeaderInfo
        {
            DateUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            Server = "PROD01", Database = "Sales", Operator = "DOMAIN\\rudi",
            Reason = "0 reads in 92 days", Stats = IndexUsageStats.Empty
        };

        var fileName = BackupWriter.WriteIndexBackup(_dir, index, "CREATE NONCLUSTERED INDEX [IX_Legacy] ...;", header);

        Assert.Equal("Sales.dbo.Orders.IX_Legacy.sql", fileName);
        var content = File.ReadAllText(Path.Combine(_dir, fileName));
        Assert.Contains("-- Server: PROD01", content);
        Assert.Contains("-- Reason: 0 reads in 92 days", content);
        Assert.Contains("-- LastRead (UTC): never", content);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Legacy]", content);
    }

    [Fact]
    public void Colliding_names_get_a_numeric_suffix_and_never_overwrite()
    {
        // Two distinct index names that sanitize to the same file name.
        var a = new IndexModel { Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX.A", Type = IndexType.NonclusteredRowstore };
        var b = new IndexModel { Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A", Type = IndexType.NonclusteredRowstore };
        var header = new BackupHeaderInfo
        {
            DateUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            Server = "PROD01", Database = "Sales", Operator = "op", Reason = "r", Stats = IndexUsageStats.Empty
        };

        var first = BackupWriter.WriteIndexBackup(_dir, a, "CREATE ... [IX.A];", header);
        var second = BackupWriter.WriteIndexBackup(_dir, b, "CREATE ... [IX_A];", header);

        Assert.Equal("Sales.dbo.Orders.IX_A.sql", first);
        Assert.Equal("Sales.dbo.Orders.IX_A (2).sql", second);
        Assert.Contains("[IX.A]", File.ReadAllText(Path.Combine(_dir, first)));   // first not overwritten
    }
}
