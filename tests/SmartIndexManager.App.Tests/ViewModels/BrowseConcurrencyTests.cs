using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class BrowseConcurrencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-browseconc-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Overlapping_detail_loads_never_run_concurrently_on_the_provider()
    {
        var probe = new ConcurrencyProbeProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered(name: "IX_A"), IndexModelFactory.Nonclustered(name: "IX_B")]
        };
        var paths = new AppPaths(_dir, _dir, _dir);
        var basket = new DeletionBasket();
        var dryRun = new DryRunViewModel(basket, paths, new ResxLocalizer());
        var basketVm = new DeletionBasketViewModel(basket, new DeletionOrchestrator(Path.Combine(_dir, "audit.log")), dryRun, paths, new ResxLocalizer());
        var vm = new BrowseViewModel(new IndexGridViewModel(), basketVm, paths, new ResxLocalizer());
        var rows = probe.Indexes.Select(i => new IndexRowViewModel(i, null, new Core.Safety.SafetyAssessment(Core.Safety.DeletionEligibility.Deletable, null, []), false, false)).ToList();
        await vm.OnConnectedAsync(probe, rows, CancellationToken.None);

        var t1 = vm.ShowDetailAsync(rows[0]);
        var t2 = vm.ShowDetailAsync(rows[1]);
        var t3 = vm.ShowDetailAsync(rows[0]);
        probe.Release();
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(1, probe.MaxConcurrent);
    }
}
