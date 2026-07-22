using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Deletion;

public class DeletionBasketTests
{
    private static IndexModel Nc() => new()
    {
        Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
        Type = IndexType.NonclusteredRowstore
    };

    private static SafetyAssessment Deletable() => new(DeletionEligibility.Deletable, null, []);
    private static SafetyAssessment NotDeletable() => new(DeletionEligibility.NotDeletable, "unique", []);

    [Fact]
    public void Add_deletable_index_succeeds()
    {
        var basket = new DeletionBasket();
        var result = basket.Add(Nc(), Deletable());
        Assert.True(result.Success);
        Assert.Single(basket.Entries);
    }

    [Fact]
    public void Add_not_deletable_index_fails()
    {
        var basket = new DeletionBasket();
        var result = basket.Add(Nc(), NotDeletable());
        Assert.False(result.Success);
        Assert.Empty(basket.Entries);
    }

    [Fact]
    public void Remove_clears_entry()
    {
        var basket = new DeletionBasket();
        var index = Nc();
        basket.Add(index, Deletable());
        basket.Remove(index);
        Assert.Empty(basket.Entries);
    }
}
