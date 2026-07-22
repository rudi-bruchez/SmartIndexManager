using SmartIndexManager.Core.Persistence;

namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionResult(IReadOnlyList<IndexDeletionResult> Results);
public sealed record IndexDeletionResult(
    string Database, string Schema, string Table, string Index,
    IndexDeletionStatus Status, string? Error);
