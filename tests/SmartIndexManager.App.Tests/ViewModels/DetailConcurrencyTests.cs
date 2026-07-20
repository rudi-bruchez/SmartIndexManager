using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

// The SQL Server provider holds a single connection without MARS, so two detail loads
// must never issue overlapping commands on it. These tests pin that invariant.
public class DetailConcurrencyTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-detailconc-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // A provider that records the maximum number of GetQueryUsageAsync calls in flight
    // at once, mimicking a connection that cannot run concurrent commands.
    private sealed class ConcurrencyProbeProvider : IIndexProvider
    {
        private int _current;
        public int MaxConcurrent { get; private set; }
        private readonly TaskCompletionSource _gate = new();
        public void Release() => _gate.TrySetResult();

        public required ServerInfo ServerInfo { get; init; }
        public required ProviderCapabilities Capabilities { get; init; }
        public required PermissionReport Permissions { get; init; }
        public IReadOnlyList<IndexModel> Indexes { get; init; } = [];

        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct = default)
            => Task.FromResult(Indexes);

        public async Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
        {
            // Deliberately ignores the token while parked: models a command already on the
            // wire that cancellation cannot abort instantly. Overlap here is the real bug.
            var now = Interlocked.Increment(ref _current);
            MaxConcurrent = Math.Max(MaxConcurrent, now);
            try { await _gate.Task; }
            finally { Interlocked.Decrement(ref _current); }
            return [];
        }

        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IndexHint>>([]);

        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
            => Task.FromResult(QueryStoreState.Off);

        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
            => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private MainWindowViewModel Build(ConcurrencyProbeProvider probe)
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.WindowsIntegrated },
            DatabasesText = "Sales"
        };
        return new MainWindowViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(probe), paths),
            new NullPasswordPrompt(), connections, new IndexGridViewModel(),
            new PermissionStatusViewModel(new ResxLocalizer()),
            paths, new ThemeService(paths), new ResxLocalizer());
    }

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
        var vm = Build(probe);
        await vm.ConnectCommand.ExecuteAsync(null);
        var rows = vm.Grid.View.Cast<IndexRowViewModel>().ToList();

        var t1 = vm.ShowDetailAsync(rows[0]);   // parks inside the provider, one command in flight
        var t2 = vm.ShowDetailAsync(rows[1]);   // must wait for the first to unwind before starting
        var t3 = vm.ShowDetailAsync(rows[0]);   // a third rapid change must not break the invariant
        probe.Release();
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(1, probe.MaxConcurrent);
    }
}
