using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Scoring;

public sealed record ScoreInputs
{
    public required IndexModel Index { get; init; }
    public required int InstanceUptimeDays { get; init; }
    public bool SupportsForeignKey { get; init; }
    public bool ReferencedByHint { get; init; }
    public bool IsRedundant { get; init; }
    public required DateTime NowUtc { get; init; }
}
