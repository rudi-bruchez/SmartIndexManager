using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionSessionViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-session-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubPrompt(string? pw) : IPasswordPrompt
    {
        public Task<string?> RequestAsync(string name, CancellationToken ct) => Task.FromResult(pw);
    }

    private sealed class RecordingDialogs : IDialogService
    {
        public int ShownCount { get; private set; }
        public Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm) { ShownCount++; return Task.CompletedTask; }
    }

    private sealed class ThrowingFactory : IIndexProviderFactory
    {
        public Task<IIndexProvider> ConnectAsync(ConnectionRequest request, string? password, CancellationToken ct = default)
            => throw new InvalidOperationException("connection refused");
    }

    private sealed class DelegateFactory : IIndexProviderFactory
    {
        private readonly Func<ConnectionRequest, string?, CancellationToken, Task<IIndexProvider>> _connect;
        public DelegateFactory(Func<ConnectionRequest, string?, CancellationToken, Task<IIndexProvider>> connect)
            => _connect = connect;

        public Task<IIndexProvider> ConnectAsync(ConnectionRequest request, string? password, CancellationToken ct = default)
            => _connect(request, password, ct);
    }

    private sealed class ImmediateProvider : IIndexProvider
    {
        public bool Disposed { get; private set; }

        public ServerInfo ServerInfo { get; init; } = new()
        {
            ServerName = "PROD01",
            ProductVersion = new Version(16, 0),
            Edition = "Developer",
            Platform = ServerPlatform.OnPremises,
            UptimeDays = 100
        };

        public ProviderCapabilities Capabilities { get; init; } = new();
        public PermissionReport Permissions { get; init; } = new() { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };
        public IReadOnlyList<IndexModel> Indexes { get; init; } = [IndexModelFactory.Nonclustered()];

        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct = default)
            => Task.FromResult(Indexes);

        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QueryUsage>>([]);

        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IndexHint>>([]);

        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
            => Task.FromResult(QueryStoreState.Off);

        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TableExistsAsync(string database, string schema, string table, CancellationToken ct = default)
            => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DelayedProvider : IIndexProvider
    {
        private readonly TaskCompletionSource _started = new();
        private readonly TaskCompletionSource _release = new();

        public Task StartedTask => _started.Task;
        public void Release() => _release.TrySetResult();
        public bool Disposed { get; private set; }

        public ServerInfo ServerInfo { get; init; } = new()
        {
            ServerName = "PROD01",
            ProductVersion = new Version(16, 0),
            Edition = "Developer",
            Platform = ServerPlatform.OnPremises,
            UptimeDays = 100
        };

        public ProviderCapabilities Capabilities { get; init; } = new();
        public PermissionReport Permissions { get; init; } = new() { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };
        public IReadOnlyList<IndexModel> Indexes { get; init; } = [IndexModelFactory.Nonclustered()];

        public async Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(ct);
            return Indexes;
        }

        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<QueryUsage>>([]);

        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IndexHint>>([]);

        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
            => Task.FromResult(QueryStoreState.Off);

        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<bool> TableExistsAsync(string database, string schema, string table, CancellationToken ct = default)
            => Task.FromResult(true);

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private (ConnectionSessionViewModel vm, RecordingDialogs dialogs) Build(string? password = "pw")
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities(),
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var dialogs = new RecordingDialogs();
        var vm = new ConnectionSessionViewModel(
            new IndexLoadService(new FakeIndexProviderFactory(provider), paths),
            new StubPrompt(password), connections, dialogs, new ResxLocalizer());
        return (vm, dialogs);
    }

    private ConnectionSessionViewModel BuildFailing()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ConnectionSessionViewModel(
            new IndexLoadService(new ThrowingFactory(), paths),
            new StubPrompt("pw"), connections, dialogs, new ResxLocalizer());
        return vm;
    }

    private ConnectionSessionViewModel BuildWithFactory(IIndexProviderFactory factory, string? password = "pw")
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(new ResxLocalizer(), true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var dialogs = new RecordingDialogs();
        var vm = new ConnectionSessionViewModel(
            new IndexLoadService(factory, paths),
            new StubPrompt(password), connections, dialogs, new ResxLocalizer());
        return vm;
    }

    [Fact]
    public async Task Connect_sets_connected_and_raises_Connected_with_rows()
    {
        var (vm, _) = Build();
        LoadResult? seen = null;
        vm.Connected += r => { seen = r; return Task.CompletedTask; };

        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.True(vm.IsConnected);
        Assert.NotNull(vm.ActiveProvider);
        Assert.NotNull(seen);
        Assert.Single(seen!.Rows);
    }

    [Fact]
    public async Task Disconnect_disposes_provider_and_raises_Disconnected()
    {
        var (vm, _) = Build();
        await vm.ConnectCommand.ExecuteAsync(null);
        var raised = false;
        vm.Disconnected += () => { raised = true; return Task.CompletedTask; };

        await vm.DisconnectCommand.ExecuteAsync(null);

        Assert.False(vm.IsConnected);
        Assert.Null(vm.ActiveProvider);
        Assert.True(raised);
    }

    [Fact]
    public async Task Manage_invokes_the_dialog_service()
    {
        var (vm, dialogs) = Build();
        await vm.ManageCommand.ExecuteAsync(null);
        Assert.Equal(1, dialogs.ShownCount);
    }

    [Fact]
    public async Task Connect_does_nothing_when_the_password_prompt_is_cancelled()
    {
        var (vm, _) = Build(password: null);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.False(vm.IsConnected);
        Assert.Null(vm.ActiveProvider);
    }

    [Fact]
    public async Task Connect_sets_error_status_and_clears_busy_on_failure()
    {
        var vm = BuildFailing();
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(Strings.Connection_Error, vm.StatusMessage);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Rapid_double_connect_serializes_and_disposes_cancelled_provider()
    {
        var provider1 = new DelayedProvider();
        var provider2 = new ImmediateProvider();
        var providers = new IIndexProvider[] { provider1, provider2 };
        var index = 0;
        var factory = new DelegateFactory((_, _, _) =>
            Task.FromResult<IIndexProvider>(providers[System.Threading.Interlocked.Increment(ref index) - 1]));

        var vm = BuildWithFactory(factory);

        var t1 = vm.ConnectCommand.ExecuteAsync(null);
        var t2 = vm.ConnectCommand.ExecuteAsync(null);
        await Task.WhenAll(t1, t2);

        Assert.Same(provider2, vm.ActiveProvider);
        Assert.True(vm.IsConnected);
        Assert.True(provider1.Disposed);
        Assert.False(provider2.Disposed);
    }

    [Fact]
    public async Task Disconnect_while_loading_cancels_and_leaves_disconnected()
    {
        var provider = new DelayedProvider();
        var factory = new DelegateFactory((_, _, _) => Task.FromResult<IIndexProvider>(provider));
        var vm = BuildWithFactory(factory);

        var connectTask = vm.ConnectCommand.ExecuteAsync(null);
        await provider.StartedTask;

        await vm.DisconnectCommand.ExecuteAsync(null);
        await connectTask;

        Assert.False(vm.IsConnected);
        Assert.Null(vm.ActiveProvider);
        Assert.True(provider.Disposed);
    }

    [Fact]
    public async Task Rapid_cancel_calls_do_not_throw_object_disposed()
    {
        var provider = new DelayedProvider();
        var factory = new DelegateFactory((_, _, _) => Task.FromResult<IIndexProvider>(provider));
        var vm = BuildWithFactory(factory);

        var connectTask = vm.ConnectCommand.ExecuteAsync(null);
        await provider.StartedTask;

        Exception? captured = null;
        for (var i = 0; i < 100; i++)
        {
            captured ??= Record.Exception(() => vm.CancelCommand.Execute(null));
        }

        provider.Release();
        await connectTask;

        Assert.Null(captured);
        Assert.False(vm.IsConnected);
    }
}
