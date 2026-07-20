using SmartIndexManager.Core.Audit;
using Xunit;

namespace SmartIndexManager.Core.Tests.Audit;

public class AuditLogTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-audit-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Append_writes_one_json_line_per_entry_and_reads_them_back()
    {
        var path = Path.Combine(_dir, "audit.jsonl");
        AuditLog.Append(path, new AuditEntry(
            new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
            AuditAction.Drop, "PROD01", "Sales", "DOMAIN\\rudi", "Dropped IX_Orders_Legacy"));
        AuditLog.Append(path, new AuditEntry(
            new DateTime(2026, 07, 20, 10, 5, 0, DateTimeKind.Utc),
            AuditAction.Restore, "PROD01", "Sales", "DOMAIN\\rudi", "Restored IX_Orders_Legacy"));

        Assert.Equal(2, File.ReadAllLines(path).Length); // one line per entry (JSONL)

        var entries = AuditLog.Read(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal(AuditAction.Drop, entries[0].Action);
        Assert.Equal("Restored IX_Orders_Legacy", entries[1].Detail);
    }
}
