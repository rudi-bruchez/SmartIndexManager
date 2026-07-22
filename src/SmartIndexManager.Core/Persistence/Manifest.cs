namespace SmartIndexManager.Core.Persistence;

public enum DeletionMode { Execute, Script }
public enum IndexDeletionStatus { Dropped, Failed, Scripted, Pending, Restored }

public sealed record ManifestStats
{
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
    public double SizeMb { get; init; }
}

public sealed record ManifestIndexEntry
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public required string File { get; init; }
    public required string Reason { get; init; }
    public string Comment { get; init; } = "";
    public int Score { get; init; }
    public ManifestStats Stats { get; init; } = new();
    public IndexDeletionStatus Status { get; init; }
    public DateTime? RestoredUtc { get; init; }
}

public sealed record Manifest
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Tool { get; init; } = "SmartIndexManager";
    public required string ToolVersion { get; init; }
    public required DateTime CreatedUtc { get; init; }
    public required string Server { get; init; }
    public required string Operator { get; init; }
    public int InstanceUptimeDays { get; init; }
    public DeletionMode Mode { get; init; }
    public IReadOnlyList<ManifestIndexEntry> Indexes { get; init; } = [];
}
