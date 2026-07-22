namespace SmartIndexManager.Core.Restore;

public sealed record RestoreResult(
    IReadOnlyList<RestoreEntryResult> Restored,
    IReadOnlyList<RestoreEntryResult> Failed);

public sealed record RestoreEntryResult(
    string Database, string Schema, string Table, string Index, bool Success, string? Error);
