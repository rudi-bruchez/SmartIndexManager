using SmartIndexManager.Core.Persistence;
using Xunit;

namespace SmartIndexManager.Core.Tests.Persistence;

public class ManifestStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-manifest-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static Manifest Sample() => new()
    {
        ToolVersion = "1.0.0",
        CreatedUtc = new DateTime(2026, 07, 20, 10, 0, 0, DateTimeKind.Utc),
        Server = "PROD01", Operator = "DOMAIN\\rudi", InstanceUptimeDays = 92,
        Mode = DeletionMode.Execute,
        Indexes =
        [
            new ManifestIndexEntry
            {
                Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_Orders_Legacy",
                File = "Sales.dbo.Orders.IX_Orders_Legacy.sql",
                Reason = "0 reads in 92 days", Score = 94,
                Stats = new ManifestStats { Updates = 1200000, SizeMb = 830 },
                Status = IndexDeletionStatus.Dropped
            }
        ]
    };

    [Fact]
    public void Round_trips_with_schema_version()
    {
        var path = Path.Combine(_dir, "manifest.json");
        ManifestStore.Write(path, Sample());

        var text = File.ReadAllText(path);
        Assert.Contains("\"schemaVersion\": 1", text);
        Assert.Contains("\"status\": \"dropped\"", text);   // camelCase, matches design spec section 9
        Assert.Contains("\"mode\": \"execute\"", text);

        var read = ManifestStore.Read(path);
        Assert.Equal("PROD01", read.Server);
        Assert.Single(read.Indexes);
        Assert.Equal("IX_Orders_Legacy", read.Indexes[0].Index);
    }

    [Fact]
    public void MarkRestored_sets_the_timestamp_on_the_matching_index()
    {
        var when = new DateTime(2026, 07, 21, 8, 0, 0, DateTimeKind.Utc);
        var updated = ManifestStore.MarkRestored(Sample(), "Sales", "dbo", "Orders", "IX_Orders_Legacy", when);
        Assert.Equal(when, updated.Indexes[0].RestoredUtc);
    }

    [Fact]
    public void Pending_status_round_trips()
    {
        var manifest = Sample() with
        {
            Indexes =
            [
                Sample().Indexes[0] with { Status = IndexDeletionStatus.Pending }
            ]
        };
        var path = Path.Combine(_dir, "pending.json");
        ManifestStore.Write(path, manifest);
        var read = ManifestStore.Read(path);
        Assert.Equal(IndexDeletionStatus.Pending, read.Indexes[0].Status);
    }

    [Fact]
    public void Restored_status_round_trips()
    {
        var manifest = Sample() with
        {
            Indexes =
            [
                Sample().Indexes[0] with { Status = IndexDeletionStatus.Restored, RestoredUtc = DateTime.UtcNow }
            ]
        };
        var path = Path.Combine(_dir, "restored.json");
        ManifestStore.Write(path, manifest);
        var read = ManifestStore.Read(path);
        Assert.Equal(IndexDeletionStatus.Restored, read.Indexes[0].Status);
    }
}
