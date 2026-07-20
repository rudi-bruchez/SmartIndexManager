using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexDetailViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-detail-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task ShowAsync_fills_ddl_queries_hints_and_score_factors()
    {
        var index = IndexModelFactory.Nonclustered(name: "IX_Detail");
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Usage = [new QueryUsage("SELECT ...", 42, DateTime.UtcNow, UsageSource.PlanCache)],
            Hints = [new IndexHint("dbo.GetOrders", "query hint")]
        };
        var row = new IndexRowViewModel(index,
            new ConfidenceScore(88, ScoreColor.Green, [new ScoreFactor("no-reads", "0 reads since instance start")]),
            new SafetyAssessment(DeletionEligibility.Deletable, null, []), false, false);

        var vm = new IndexDetailViewModel(provider, new AppPaths(_dir, _dir, _dir), new ResxLocalizer());
        await vm.ShowAsync(row, CancellationToken.None);

        Assert.Contains("IX_Detail", vm.Ddl);
        Assert.Single(vm.Queries);
        Assert.Single(vm.Hints);
        Assert.Contains(vm.ScoreFactors, f => f.Name == "no-reads");
    }
}
