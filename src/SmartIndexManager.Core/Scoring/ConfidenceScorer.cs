using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Scoring;

public sealed class ConfidenceScorer
{
    private readonly ScoringOptions _options;

    public ConfidenceScorer(ScoringOptions? options = null) => _options = options ?? new ScoringOptions();

    public ConfidenceScore Score(ScoreInputs inputs)
    {
        var factors = new List<ScoreFactor>();
        double value = BaseReadScore(inputs, factors);

        // Bonuses first.
        if (inputs.IsRedundant)
        {
            value += _options.RedundancyBonus;
            factors.Add(new ScoreFactor("redundant", $"+{_options.RedundancyBonus} redundant with another index"));
        }
        if (inputs.Index.Usage.Updates > 0 && inputs.Index.Usage.TotalReads == 0)
        {
            value += _options.CostlyUpdatesBonus;
            factors.Add(new ScoreFactor("costly-updates",
                $"+{_options.CostlyUpdatesBonus} {inputs.Index.Usage.Updates} updates with 0 reads"));
        }

        // Caps next: a cap always wins over a bonus.
        int cap = 100;
        if (inputs.InstanceUptimeDays < _options.UptimeReliabilityThresholdDays)
        {
            cap = Math.Min(cap, _options.ShortUptimeCap);
            factors.Add(new ScoreFactor("short-uptime",
                $"cap {_options.ShortUptimeCap}, uptime {inputs.InstanceUptimeDays}d below threshold"));
        }
        if (inputs.SupportsForeignKey)
        {
            cap = Math.Min(cap, _options.FkSupportCap);
            factors.Add(new ScoreFactor("fk-support", $"cap {_options.FkSupportCap}, supports a foreign key"));
        }
        if (inputs.Index.FilterPredicate is not null)
        {
            cap = Math.Min(cap, _options.FilteredCap);
            factors.Add(new ScoreFactor("filtered", $"cap {_options.FilteredCap}, filtered index"));
        }
        if (inputs.ReferencedByHint)
        {
            cap = Math.Min(cap, _options.HintCap);
            factors.Add(new ScoreFactor("hint", $"cap {_options.HintCap}, referenced by a hint or plan guide"));
        }

        value = Math.Min(value, cap);
        int final = (int)Math.Round(Math.Clamp(value, 0, 100));
        return new ConfidenceScore(final, Colorize(final), factors);
    }

    private double BaseReadScore(ScoreInputs inputs, List<ScoreFactor> factors)
    {
        long reads = inputs.Index.Usage.TotalReads;
        if (reads == 0)
        {
            factors.Add(new ScoreFactor("no-reads", "0 reads since instance start"));
            return 100;
        }

        double freshness = FreshnessFactor(inputs.Index.Usage.LastRead, inputs.NowUtc);
        double weightedReads = reads * freshness;
        double drop = _options.ReadWeightMultiplier * Math.Log10(1 + weightedReads);
        double baseScore = 100 - Math.Min(100, drop);
        factors.Add(new ScoreFactor("reads",
            $"{reads} reads, freshness {freshness:0.00}, base {baseScore:0}"));
        return baseScore;
    }

    // Recent reads weigh full (1.0); reads age down linearly to MinFreshnessFactor over the window.
    private double FreshnessFactor(DateTime? lastRead, DateTime nowUtc)
    {
        if (lastRead is null) return 1.0;
        double ageDays = Math.Max(0, (nowUtc - lastRead.Value).TotalDays);
        double factor = 1.0 - (ageDays / _options.FreshnessWindowDays);
        return Math.Clamp(factor, _options.MinFreshnessFactor, 1.0);
    }

    private ScoreColor Colorize(int value)
        => value >= _options.GreenThreshold ? ScoreColor.Green
         : value >= _options.OrangeThreshold ? ScoreColor.Orange
         : ScoreColor.Red;
}
