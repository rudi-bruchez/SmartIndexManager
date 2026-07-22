namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionOptions(TimeSpan DropTimeout, string? Comment = null);
