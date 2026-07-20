namespace SmartIndexManager.App.Services;

public interface IAppPaths
{
    string ConfigDir { get; }          // per-platform app config directory
    string SnapshotRoot { get; }       // <ConfigDir>
    string DefaultBackupRoot { get; }  // <Documents>/SmartIndexManager
    string SqlScriptRoot { get; }      // directory holding sql/sqlserver/*.sql
}
