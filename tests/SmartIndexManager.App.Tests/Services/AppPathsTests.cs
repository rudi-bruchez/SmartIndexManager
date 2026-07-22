using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Tests.Services;

public class AppPathsTests
{
    [Fact]
    public void Derives_snapshot_and_backup_roots_from_the_base_dirs()
    {
        var paths = new AppPaths(configDir: "/cfg", documentsDir: "/docs", sqlScriptRoot: "/app/sql/sqlserver");

        Assert.Equal("/cfg", paths.ConfigDir);
        Assert.Equal("/cfg", paths.SnapshotRoot);
        Assert.Equal(Path.Combine("/docs", "SmartIndexManager"), paths.DefaultBackupRoot);
        Assert.Equal("/app/sql/sqlserver", paths.SqlScriptRoot);
    }

    [Fact]
    public void Default_resolves_a_config_dir_under_the_app_name()
    {
        var paths = AppPaths.Default();
        Assert.Contains("SmartIndexManager", paths.ConfigDir);
        Assert.EndsWith(Path.Combine("sql", "sqlserver"), paths.SqlScriptRoot);
    }

    [Fact]
    public void Overrides_use_settings_when_provided()
    {
        var paths = new AppPaths(configDir: "/cfg", documentsDir: "/docs", sqlScriptRoot: "/sql",
            settings: new AppSettings { DefaultBackupRoot = "/custom/backups", SnapshotRoot = "/custom/snaps" });
        Assert.Equal("/custom/backups", paths.DefaultBackupRoot);
        Assert.Equal("/custom/snaps", paths.SnapshotRoot);
    }

    [Fact]
    public void ReloadSettings_re_reads_settings_file_and_updates_roots()
    {
        var dir = Directory.CreateTempSubdirectory("sim-apppaths-").FullName;
        try
        {
            new SettingsService().Save(dir, new AppSettings
            {
                DefaultBackupRoot = "/updated/backups",
                SnapshotRoot = "/updated/snaps"
            });

            var paths = new AppPaths(configDir: dir, documentsDir: "/docs", sqlScriptRoot: "/sql");
            Assert.NotEqual("/updated/backups", paths.DefaultBackupRoot);

            paths.ReloadSettings();

            Assert.Equal("/updated/backups", paths.DefaultBackupRoot);
            Assert.Equal("/updated/snaps", paths.SnapshotRoot);
            Assert.Equal("/updated/backups", paths.Settings.DefaultBackupRoot);
            Assert.Equal("/updated/snaps", paths.Settings.SnapshotRoot);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
