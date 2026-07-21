using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexRowViewModelScoreColorTests
{
    private static IndexRowViewModel Row(ScoreColor color)
    {
        var index = IndexModelFactory.Nonclustered();
        var score = new ConfidenceScore(90, color, []);
        var safety = new SafetyAssessment(DeletionEligibility.Deletable, null, []);
        return new IndexRowViewModel(index, score, safety, isRedundant: false, isReferencedByHint: false);
    }

    [Fact]
    public void Green_maps_to_safe()
    {
        var r = Row(ScoreColor.Green);
        Assert.True(r.IsScoreSafe);
        Assert.False(r.IsScoreCaution);
        Assert.False(r.IsScoreRisk);
    }

    [Fact]
    public void Orange_maps_to_caution()
    {
        var r = Row(ScoreColor.Orange);
        Assert.True(r.IsScoreCaution);
        Assert.False(r.IsScoreSafe);
    }

    [Fact]
    public void Red_maps_to_risk()
    {
        var r = Row(ScoreColor.Red);
        Assert.True(r.IsScoreRisk);
        Assert.False(r.IsScoreSafe);
    }
}
