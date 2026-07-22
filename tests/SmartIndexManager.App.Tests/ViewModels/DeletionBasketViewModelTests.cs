using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class DeletionBasketViewModelTests
{
    [Fact]
    public void Add_adds_deletable_index()
    {
        var basket = new DeletionBasket();
        var vm = new DeletionBasketViewModel(basket, null!, new DryRunViewModel(basket, new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer()), new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer());
        var index = IndexModelFactory.Nonclustered();
        vm.Add(index, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));
        Assert.Single(vm.Entries);
    }
}
