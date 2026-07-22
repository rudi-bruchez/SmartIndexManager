using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Services;

public sealed class AppPaths : IAppPaths
{
    public string ConfigDir { get; }
    public string SnapshotRoot { get; }
    public string DefaultBackupRoot { get; }
    public string SqlScriptRoot { get; }
    public AppSettings Settings { get; }

    public AppPaths(string configDir, string documentsDir, string sqlScriptRoot, AppSettings? settings = null)
    {
        Settings = settings ?? new AppSettings();
        ConfigDir = configDir;
        // SnapshotStore appends its own "snapshots" segment, so the root is the config dir by default.
        SnapshotRoot = Settings.SnapshotRoot ?? configDir;
        DefaultBackupRoot = Settings.DefaultBackupRoot ?? Path.Combine(documentsDir, "SmartIndexManager");
        SqlScriptRoot = sqlScriptRoot;
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
