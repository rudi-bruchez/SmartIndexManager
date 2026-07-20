using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class MainWindowViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-main-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class StubPrompt(string? password) : IPasswordPrompt
    {
        public Task<string?> RequestAsync(string name, CancellationToken ct) => Task.FromResult(password);
    }

    private MainWindowViewModel Build(string? password)
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var paths = new AppPaths(_dir, _dir, _dir);
        var store = new ConnectionStore(paths);
        store.Save([new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" }]);

        var connections = new ConnectionManagerViewModel(store, new AuthAvailability(true, false))
        {
            Selected = new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            DatabasesText = "Sales"
        };
        var factory = new FakeIndexProviderFactory(provider);
        return new MainWindowViewModel(
            new IndexLoadService(factory, paths),
            new StubPrompt(password), connections, new IndexGridViewModel(),
            new PermissionStatusViewModel(new ResxLocalizer()),
            paths,
            new ResxLocalizer());
    }

    [Fact]
    public async Task Connect_loads_rows_into_the_grid_and_updates_permissions()
    {
        var vm = Build(password: "pw");
        await vm.ConnectCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.Grid.VisibleCount);
        Assert.False(vm.Permissions.UsageAvailable);   // provider reported CanViewState=false
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task Connect_does_nothing_when_the_password_prompt_is_cancelled()
    {
        var vm = Build(password: null);
        await vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(0, vm.Grid.VisibleCount);
    }
}
