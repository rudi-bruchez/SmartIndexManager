namespace SmartIndexManager.Core.Scoring;

public sealed record ScoringOptions
{
    public int UptimeReliabilityThresholdDays { get; init; } = 30;
    public int ShortUptimeCap { get; init; } = 60;
    public int FkSupportCap { get; init; } = 40;
    public int FilteredCap { get; init; } = 50;
    public int HintCap { get; init; } = 10;
    public int RedundancyBonus { get; init; } = 10;
    public int CostlyUpdatesBonus { get; init; } = 10;
    public int FreshnessWindowDays { get; init; } = 90;
    public double ReadWeightMultiplier { get; init; } = 20.0;
    public double MinFreshnessFactor { get; init; } = 0.25;
    public int GreenThreshold { get; init; } = 80;
    public int OrangeThreshold { get; init; } = 50;
}
