using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexRowViewModelTests
{
    [Fact]
    public void Exposes_identity_columns_and_key_summary()
    {
        var index = IndexModelFactory.Nonclustered(keyColumns: ["CustomerId", "OrderDate"], includedColumns: ["Total"]);
        var row = new IndexRowViewModel(index,
            score: new ConfidenceScore(90, ScoreColor.Green, []),
            safety: new SafetyAssessment(DeletionEligibility.Deletable, null, []),
            isRedundant: false, isReferencedByHint: false);

        Assert.Equal("Sales", row.Database);
        Assert.Equal("Orders", row.Table);
        Assert.Equal("CustomerId, OrderDate", row.KeySummary);
        Assert.Equal("Total", row.IncludeSummary);
        Assert.Equal(90, row.Score);
        Assert.False(row.NotDeletable);
    }

    [Fact]
    public void Not_deletable_index_sets_the_badge_and_hides_the_score()
    {
        var index = IndexModelFactory.PrimaryKey();
        var row = new IndexRowViewModel(index,
            score: null,
            safety: new SafetyAssessment(DeletionEligibility.NotDeletable, null, []),
            isRedundant: false, isReferencedByHint: false);

        Assert.True(row.NotDeletable);
        Assert.Null(row.Score);
    }

    [Fact]
    public void Not_deletable_index_exposes_the_block_reason_for_the_badge_tooltip()
    {
        var index = IndexModelFactory.PrimaryKey();
        var row = new IndexRowViewModel(index,
            score: null,
            safety: new SafetyAssessment(DeletionEligibility.NotDeletable, "Primary key constraint", []),
            isRedundant: false, isReferencedByHint: false);

        Assert.Equal("Primary key constraint", row.NotDeletableReason);
    }

    [Fact]
    public void Badges_reflect_redundancy_and_hint_flags()
    {
        var row = new IndexRowViewModel(IndexModelFactory.Nonclustered(),
            score: new ConfidenceScore(70, ScoreColor.Orange, []),
            safety: new SafetyAssessment(DeletionEligibility.Deletable, null, []),
            isRedundant: true, isReferencedByHint: true);

        Assert.True(row.Redundant);
        Assert.True(row.ReferencedByHint);
    }
}
