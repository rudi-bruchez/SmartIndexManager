using SmartIndexManager.Core.Persistence;
using Xunit;

namespace SmartIndexManager.Core.Tests.Persistence;

public class SnapshotStoreTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sim-snap-").FullName;
    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static UsageSnapshot Snap(DateTime capturedUtc) => new()
    {
        Server = "PROD01", Database = "Sales", CapturedUtc = capturedUtc, InstanceUptimeDays = 40,
        Indexes = [new SnapshotIndexUsage { Schema = "dbo", Table = "Orders", Index = "IX", Seeks = 5 }]
    };

    [Fact]
    public void Write_then_read_all_round_trips()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        var all = SnapshotStore.ReadAll(_root, "PROD01", "Sales");
        Assert.Equal(2, all.Count);
        Assert.All(all, s => Assert.Equal(1, s.SchemaVersion));
    }

    [Fact]
    public void Oldest_capture_returns_earliest_timestamp()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        Assert.Equal(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc),
            SnapshotStore.OldestCaptureUtc(_root, "PROD01", "Sales"));
    }

    [Fact]
    public void Purge_removes_captures_older_than_cutoff()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc)));
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 06, 01, 0, 0, 0, DateTimeKind.Utc)));

        int purged = SnapshotStore.PurgeOlderThan(_root, "PROD01", "Sales",
            new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, purged);
        Assert.Single(SnapshotStore.ReadAll(_root, "PROD01", "Sales"));
    }

    [Fact]
    public void Oldest_capture_is_null_when_none_exist()
        => Assert.Null(SnapshotStore.OldestCaptureUtc(_root, "PROD01", "Sales"));

    [Fact]
    public void Read_all_skips_malformed_json_files()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 05, 01, 0, 0, 0, DateTimeKind.Utc)));
        File.WriteAllText(Path.Combine(_root, "snapshots", "PROD01", "Sales", "bad.json"), "not json");

        var all = SnapshotStore.ReadAll(_root, "PROD01", "Sales");
        Assert.Single(all);
    }

    [Fact]
    public void Purge_skips_malformed_json_files()
    {
        SnapshotStore.Write(_root, Snap(new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc)));
        File.WriteAllText(Path.Combine(_root, "snapshots", "PROD01", "Sales", "bad.json"), "not json");

        int purged = SnapshotStore.PurgeOlderThan(_root, "PROD01", "Sales",
            new DateTime(2026, 03, 01, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, purged);
        Assert.Empty(SnapshotStore.ReadAll(_root, "PROD01", "Sales"));
    }
}
