using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class BrowseViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-browse-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeIndexProvider Provider(params IndexModel[] indexes) => Provider(null, indexes);

    private static FakeIndexProvider Provider(Exception? queryUsageException, params IndexModel[] indexes) => new()
    {
        ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
        Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
        Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
        Indexes = indexes,
        QueryUsageException = queryUsageException
    };

    private BrowseViewModel Build() =>
        new(new IndexGridViewModel(), BasketViewModel(), new AppPaths(_dir, _dir, _dir), new ResxLocalizer());

    private DeletionBasketViewModel BasketViewModel()
    {
        var basket = new DeletionBasket();
        var paths = new AppPaths(_dir, _dir, _dir);
        return new DeletionBasketViewModel(basket, new DeletionOrchestrator(Path.Combine(_dir, "audit.log")), new DryRunViewModel(basket, paths, new ResxLocalizer()), paths, new ResxLocalizer());
    }

    [Fact]
    public void Starts_disconnected()
    {
        Assert.Equal(BrowseState.Disconnected, Build().State);
    }

    [Fact]
    public async Task OnConnected_with_rows_becomes_ready_and_fills_grid()
    {
        var vm = Build();
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(Provider(), rows, CancellationToken.None);

        Assert.Equal(BrowseState.Ready, vm.State);
        Assert.Equal(1, vm.Grid.VisibleCount);
        Assert.NotNull(vm.Detail);
    }

    [Fact]
    public async Task OnConnected_with_no_rows_becomes_empty()
    {
        var vm = Build();
        await vm.OnConnectedAsync(Provider(), Array.Empty<IndexRowViewModel>(), CancellationToken.None);
        Assert.Equal(BrowseState.Empty, vm.State);
    }

    [Fact]
    public async Task OnDisconnected_clears_grid_and_returns_to_disconnected()
    {
        var vm = Build();
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(Provider(), rows, CancellationToken.None);
        await vm.OnDisconnectedAsync();

        Assert.Equal(BrowseState.Disconnected, vm.State);
        Assert.Equal(0, vm.Grid.VisibleCount);
        Assert.Null(vm.Detail);
    }

    [Fact]
    public async Task OnDisconnected_clears_basket()
    {
        var vm = Build();
        var index = IndexModelFactory.Nonclustered();
        var rows = new[] { new IndexRowViewModel(index, null, Safe(), false, false) };
        await vm.OnConnectedAsync(Provider(index), rows, CancellationToken.None);
        vm.Basket.Add(index, Safe(), null);
        Assert.Single(vm.Basket.Entries);

        await vm.OnDisconnectedAsync();

        Assert.Empty(vm.Basket.Entries);
    }

    [Fact]
    public async Task ShowDetailAsync_sets_error_state_when_detail_load_fails()
    {
        var vm = Build();
        var provider = Provider(
            new InvalidOperationException("query store unavailable"),
            IndexModelFactory.Nonclustered());
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(provider, rows, CancellationToken.None);

        await vm.ShowDetailAsync(vm.Grid.SelectedRow ?? rows[0]);

        Assert.Equal(BrowseState.Error, vm.State);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("Failed to load detail", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("query store unavailable", vm.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowDetailAsync_clears_error_state_after_successful_load()
    {
        var vm = Build();
        var provider = Provider(IndexModelFactory.Nonclustered());
        var rows = new[] { new IndexRowViewModel(IndexModelFactory.Nonclustered(), null, Safe(), false, false) };
        await vm.OnConnectedAsync(provider, rows, CancellationToken.None);

        provider.QueryUsageException = new InvalidOperationException("transient failure");
        await vm.ShowDetailAsync(rows[0]);
        Assert.Equal(BrowseState.Error, vm.State);
        Assert.NotNull(vm.ErrorMessage);

        provider.QueryUsageException = null;
        await vm.ShowDetailAsync(rows[0]);

        Assert.Equal(BrowseState.Ready, vm.State);
        Assert.Null(vm.ErrorMessage);
    }

    private static Core.Safety.SafetyAssessment Safe() =>
        new(Core.Safety.DeletionEligibility.Deletable, null, []);
}
