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
