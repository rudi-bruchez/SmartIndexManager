using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Safety;

public sealed record SafetyInputs
{
    public required IndexModel Index { get; init; }
    public required DdlResult Ddl { get; init; }
    public bool SupportsForeignKey { get; init; }
    public bool ReferencedByHint { get; init; }
    public bool DatabaseInReplicationOrAg { get; init; }
    public required int InstanceUptimeDays { get; init; }
    public int UptimeReliabilityThresholdDays { get; init; } = 30;
}
