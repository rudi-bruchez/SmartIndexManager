using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Services;

public sealed class AppPaths : IAppPaths
{
    private readonly string _documentsDir;

    public string ConfigDir { get; }
    public string SnapshotRoot { get; private set; }
    public string DefaultBackupRoot { get; private set; }
    public string SqlScriptRoot { get; }
    public AppSettings Settings { get; private set; }

    public AppPaths(string configDir, string documentsDir, string sqlScriptRoot, AppSettings? settings = null)
    {
        _documentsDir = documentsDir;
        Settings = settings ?? new AppSettings();
        ConfigDir = configDir;
        // SnapshotStore appends its own "snapshots" segment, so the root is the config dir by default.
        SnapshotRoot = Settings.SnapshotRoot ?? configDir;
        DefaultBackupRoot = Settings.DefaultBackupRoot ?? Path.Combine(documentsDir, "SmartIndexManager");
        SqlScriptRoot = sqlScriptRoot;
    }

    public void ReloadSettings()
    {
        Settings = new SettingsService().Load(ConfigDir);
        SnapshotRoot = Settings.SnapshotRoot ?? ConfigDir;
        DefaultBackupRoot = Settings.DefaultBackupRoot ?? Path.Combine(_documentsDir, "SmartIndexManager");
    }

    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var config = Path.Combine(appData, "SmartIndexManager");
        var settings = new SettingsService().Load(config);
        var sqlRoot = Path.Combine(AppContext.BaseDirectory, "sql", "sqlserver");
        return new AppPaths(config, documents, sqlRoot, settings);
    }
}
