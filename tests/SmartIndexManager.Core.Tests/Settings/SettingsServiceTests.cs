using SmartIndexManager.Core.Settings;
using Xunit;

namespace SmartIndexManager.Core.Tests.Settings;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-settings-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Round_trips_settings()
    {
        var service = new SettingsService();
        var settings = new AppSettings { DefaultBackupRoot = "/backups", SnapshotRoot = "/snaps", SnapshotRetentionDays = 60 };
        service.Save(_dir, settings);
        var loaded = service.Load(_dir);
        Assert.Equal("/backups", loaded.DefaultBackupRoot);
        Assert.Equal("/snaps", loaded.SnapshotRoot);
        Assert.Equal(60, loaded.SnapshotRetentionDays);
    }

    [Fact]
    public void Missing_file_returns_defaults()
    {
        var loaded = new SettingsService().Load(_dir);
        Assert.Null(loaded.DefaultBackupRoot);
        Assert.Null(loaded.SnapshotRoot);
        Assert.Equal(90, loaded.SnapshotRetentionDays);
    }
}
