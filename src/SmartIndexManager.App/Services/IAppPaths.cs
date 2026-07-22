using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.Services;

public interface IAppPaths
{
    string ConfigDir { get; }
    string SnapshotRoot { get; }
    string DefaultBackupRoot { get; }
    string SqlScriptRoot { get; }
    AppSettings Settings { get; }
}
