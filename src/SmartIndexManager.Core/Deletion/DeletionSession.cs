using SmartIndexManager.Core.Persistence;

namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionSession(
    string Server,
    string Operator,
    string ToolVersion,
    int InstanceUptimeDays,
    string BackupRoot,
    DeletionMode Mode);
