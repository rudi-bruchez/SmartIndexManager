using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public sealed record RedundancyFinding(
    IndexModel Redundant,
    IndexModel CoveredBy,
    RedundancyRule Rule);
