namespace SmartIndexManager.Core.Model;

public sealed record IndexUsageStats(
    long Seeks,
    long Scans,
    long Lookups,
    long Updates,
    DateTime? LastRead,
    DateTime? LastWrite)
{
    public long TotalReads => Seeks + Scans + Lookups;
    public static IndexUsageStats Empty { get; } = new(0, 0, 0, 0, null, null);
}
