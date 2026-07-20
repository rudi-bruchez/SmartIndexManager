using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionManagerViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-cm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConnectionManagerViewModel Vm()
        => new(new ConnectionStore(new AppPaths(_dir, _dir, _dir)), new AuthAvailability(true, false));

    [Fact]
    public void Loads_persisted_profiles_on_construction()
    {
        new ConnectionStore(new AppPaths(_dir, _dir, _dir)).Save(
            [new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" }]);

        Assert.Single(Vm().Profiles);
    }

    [Fact]
    public void SelectedDatabases_splits_and_trims_the_text()
    {
        var vm = Vm();
        vm.DatabasesText = " Sales , HR ,, Ops ";
        Assert.Equal(new[] { "Sales", "HR", "Ops" }, vm.SelectedDatabases);
    }
}
