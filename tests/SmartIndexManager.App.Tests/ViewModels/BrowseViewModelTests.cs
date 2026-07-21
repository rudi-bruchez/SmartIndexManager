using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class BrowseViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-browse-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeIndexProvider Provider(params IndexModel[] indexes) => new()
    {
        ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
        Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
        Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
        Indexes = indexes
    };

    private BrowseViewModel Build() =>
        new(new IndexGridViewModel(), new AppPaths(_dir, _dir, _dir), new ResxLocalizer());

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

    private static Core.Safety.SafetyAssessment Safe() =>
        new(Core.Safety.DeletionEligibility.Deletable, null, []);
}
