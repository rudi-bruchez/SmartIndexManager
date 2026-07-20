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

    [Fact]
    public void AddCommand_creates_new_profile_with_unique_name_and_selects_it()
    {
        var vm = Vm();
        vm.AddCommand.Execute(null);

        Assert.Single(vm.Profiles);
        Assert.Equal("New connection 1", vm.Profiles[0].Name);
        Assert.Same(vm.Profiles[0], vm.Selected);
    }

    [Fact]
    public void AddCommand_increments_default_name_when_collision_exists()
    {
        var vm = Vm();
        vm.AddCommand.Execute(null);
        vm.AddCommand.Execute(null);

        Assert.Equal(2, vm.Profiles.Count);
        Assert.Equal("New connection 1", vm.Profiles[0].Name);
        Assert.Equal("New connection 2", vm.Profiles[1].Name);
    }

    [Fact]
    public void SaveCommand_upserts_selected_profile_and_persists()
    {
        var vm = Vm();
        vm.AddCommand.Execute(null);
        vm.Selected = vm.Selected! with { Server = "SRV01", Auth = AuthMode.SqlLogin, Login = "app" };

        vm.SaveCommand.Execute(null);

        var reloaded = new ConnectionStore(new AppPaths(_dir, _dir, _dir)).Load();
        Assert.Single(reloaded);
        Assert.Equal("SRV01", reloaded[0].Server);
    }

    [Fact]
    public void SaveCommand_does_nothing_when_nothing_selected()
    {
        var vm = Vm();
        vm.SaveCommand.Execute(null);

        Assert.Empty(vm.Profiles);
    }

    [Fact]
    public void ConnectCommand_sets_ProfileToConnect_to_selected_and_raises_property_changed()
    {
        var vm = Vm();
        vm.AddCommand.Execute(null);
        var profile = vm.Selected!;

        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.ConnectCommand.Execute(null);

        Assert.Same(profile, vm.ProfileToConnect);
        Assert.Contains(nameof(ConnectionManagerViewModel.ProfileToConnect), changed);
    }

    [Fact]
    public void ConnectCommand_does_nothing_when_nothing_selected()
    {
        var vm = Vm();
        vm.ConnectCommand.Execute(null);

        Assert.Null(vm.ProfileToConnect);
    }
}
