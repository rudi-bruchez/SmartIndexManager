namespace SmartIndexManager.Core.DryRun;

public sealed record DryRunReport
{
    public required string Server { get; init; }
    public IReadOnlyList<string> Databases { get; init; } = [];
    public required DateTime CreatedUtc { get; init; }
    public int UptimeDays { get; init; }
    public DryRunReliabilityBadge ReliabilityBadge { get; init; }
    public double TotalSizeMb { get; init; }
    public long TotalUpdates { get; init; }
    public IReadOnlyList<DryRunReportEntry> Entries { get; init; } = [];
}
