namespace SmartIndexManager.App.Services;

public sealed class AppPaths : IAppPaths
{
    public string ConfigDir { get; }
    public string SnapshotRoot { get; }
    public string DefaultBackupRoot { get; }
    public string SqlScriptRoot { get; }

    public AppPaths(string configDir, string documentsDir, string sqlScriptRoot)
    {
        ConfigDir = configDir;
        SnapshotRoot = Path.Combine(configDir, "snapshots");
        DefaultBackupRoot = Path.Combine(documentsDir, "SmartIndexManager");
        SqlScriptRoot = sqlScriptRoot;
    }

    // Real per-platform locations. ApplicationData maps to %APPDATA% on Windows,
    // $XDG_CONFIG_HOME (or ~/.config) on Linux, ~/Library/Application Support on macOS.
    public static AppPaths Default()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var config = Path.Combine(appData, "SmartIndexManager");
        var sqlRoot = Path.Combine(AppContext.BaseDirectory, "sql", "sqlserver");
        return new AppPaths(config, documents, sqlRoot);
    }
}
