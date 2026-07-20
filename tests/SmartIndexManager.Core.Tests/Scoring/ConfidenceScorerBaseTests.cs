using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Scoring;

public class ConfidenceScorerBaseTests
{
    private static readonly DateTime Now = new(2026, 07, 20, 0, 0, 0, DateTimeKind.Utc);

    private static ScoreInputs Inputs(IndexUsageStats usage, int uptime = 90) => new()
    {
        Index = new IndexModel
        {
            Database = "db", Schema = "dbo", Table = "T", Name = "IX",
            Type = IndexType.NonclusteredRowstore, Usage = usage
        },
        InstanceUptimeDays = uptime,
        NowUtc = Now
    };

    [Fact]
    public void Zero_reads_scores_100()
    {
        var score = new ConfidenceScorer().Score(Inputs(IndexUsageStats.Empty));
        Assert.Equal(100, score.Value);
        Assert.Equal(ScoreColor.Green, score.Color);
    }

    [Fact]
    public void Reads_lower_the_score()
    {
        var few = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(10, 0, 0, 0, Now, null)));
        var many = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1_000_000, 0, 0, 0, Now, null)));

        Assert.True(few.Value < 100);
        Assert.True(many.Value < few.Value);
        Assert.InRange(many.Value, 0, 100);
    }

    [Fact]
    public void Older_reads_weigh_less_than_recent_reads()
    {
        var recent = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1000, 0, 0, 0, Now, null)));
        var old = new ConfidenceScorer().Score(
            Inputs(new IndexUsageStats(1000, 0, 0, 0, Now.AddDays(-200), null)));

        Assert.True(old.Value > recent.Value); // stale reads are less alarming
    }
}
