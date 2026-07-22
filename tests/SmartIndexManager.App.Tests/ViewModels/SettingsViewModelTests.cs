using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Settings;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class SettingsViewModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-settingsvm-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Save_writes_settings()
    {
        var paths = new AppPaths(_dir, _dir, _dir);
        var vm = new SettingsViewModel(new SettingsService(), paths, new ResxLocalizer())
        {
            DefaultBackupRoot = "/backups",
            SnapshotRetentionDays = 60
        };
        vm.SaveCommand.Execute(null);
        var loaded = new SettingsService().Load(_dir);
        Assert.Equal("/backups", loaded.DefaultBackupRoot);
        Assert.Equal(60, loaded.SnapshotRetentionDays);
    }
}
