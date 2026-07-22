using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
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

    [Fact]
    public async Task RunDryRunAsync_resets_IsBusy_when_load_fails()
    {
        var basket = new DeletionBasket();
        var paths = new AppPaths("/cfg", "/docs", "/sql");
        var vm = new DeletionBasketViewModel(basket, null!, new DryRunViewModel(basket, paths, new ResxLocalizer()), paths, new ResxLocalizer());
        var index = IndexModelFactory.Nonclustered();
        vm.Add(index, new SafetyAssessment(DeletionEligibility.Deletable, null, []), null);

        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            QueryUsageException = new InvalidOperationException("boom")
        };
        vm.SetProvider(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.RunDryRunCommand.ExecuteAsync(null));
        Assert.False(vm.IsBusy);
    }
}
