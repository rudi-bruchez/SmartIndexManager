namespace SmartIndexManager.Core.Settings;

public sealed record AppSettings
{
    public string? DefaultBackupRoot { get; init; }
    public string? SnapshotRoot { get; init; }
    public int SnapshotRetentionDays { get; init; } = 90;
}
