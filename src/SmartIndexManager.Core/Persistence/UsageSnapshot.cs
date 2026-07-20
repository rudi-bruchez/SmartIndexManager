namespace SmartIndexManager.Core.Persistence;

public sealed record SnapshotIndexUsage
{
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
}

public sealed record UsageSnapshot
{
    public int SchemaVersion { get; init; } = 1;
    public required string Server { get; init; }
    public required string Database { get; init; }
    public required DateTime CapturedUtc { get; init; }
    public int InstanceUptimeDays { get; init; }
    public IReadOnlyList<SnapshotIndexUsage> Indexes { get; init; } = [];
}
