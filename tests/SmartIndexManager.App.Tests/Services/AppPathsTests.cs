using SmartIndexManager.App.Services;

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
}
