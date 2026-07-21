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
}
