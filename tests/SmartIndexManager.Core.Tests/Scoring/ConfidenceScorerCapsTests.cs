using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Scoring;

public class ConfidenceScorerCapsTests
{
    private static readonly DateTime Now = new(2026, 07, 20, 0, 0, 0, DateTimeKind.Utc);

    private static ScoreInputs Build(
        bool fk = false, bool hint = false, bool redundant = false,
        string? filter = null, int uptime = 90, long updates = 0, long reads = 0) => new()
    {
        Index = new IndexModel
        {
            Database = "db", Schema = "dbo", Table = "T", Name = "IX",
            Type = IndexType.NonclusteredRowstore, FilterPredicate = filter,
            Usage = new IndexUsageStats(reads, 0, 0, updates, null, null)
        },
        InstanceUptimeDays = uptime,
        SupportsForeignKey = fk, ReferencedByHint = hint, IsRedundant = redundant,
        NowUtc = Now
    };

    [Fact]
    public void Short_uptime_caps_at_60_and_turns_orange()
    {
        var score = new ConfidenceScorer().Score(Build(uptime: 5));
        Assert.Equal(60, score.Value);
        Assert.Equal(ScoreColor.Orange, score.Color);
    }

    [Fact]
    public void Fk_support_caps_at_40()
        => Assert.Equal(40, new ConfidenceScorer().Score(Build(fk: true)).Value);

    [Fact]
    public void Filtered_caps_at_50()
        => Assert.Equal(50, new ConfidenceScorer().Score(Build(filter: "Status = 1")).Value);

    [Fact]
    public void Hint_caps_at_10()
        => Assert.Equal(10, new ConfidenceScorer().Score(Build(hint: true)).Value);

    [Fact]
    public void Cap_wins_over_redundancy_bonus()
    {
        // redundant would add +10 but a hint caps at 10; the cap wins
        var score = new ConfidenceScorer().Score(Build(hint: true, redundant: true));
        Assert.Equal(10, score.Value);
    }

    [Fact]
    public void Costly_updates_bonus_cannot_exceed_100()
    {
        var score = new ConfidenceScorer().Score(Build(updates: 5_000_000));
        Assert.Equal(100, score.Value); // base 100 + bonus, clamped to 100
    }

    [Fact]
    public void Lowest_applicable_cap_wins()
    {
        // both FK (40) and filtered (50) apply => 40
        var score = new ConfidenceScorer().Score(Build(fk: true, filter: "x = 1"));
        Assert.Equal(40, score.Value);
    }

    [Fact]
    public void Costly_updates_bonus_applies_only_with_zero_reads()
    {
        var withBonus = new ConfidenceScorer().Score(Build(reads: 0, updates: 1_000_000));
        Assert.Equal(100, withBonus.Value);
        Assert.Contains(withBonus.Factors, f => f.Name == "costly-updates");
    }

    [Fact]
    public void Costly_updates_bonus_is_ignored_when_index_has_reads()
    {
        var withoutBonus = new ConfidenceScorer().Score(Build(reads: 10, updates: 1));
        Assert.DoesNotContain(withoutBonus.Factors, f => f.Name == "costly-updates");
    }
}
