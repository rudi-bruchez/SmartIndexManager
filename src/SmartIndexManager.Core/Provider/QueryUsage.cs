namespace SmartIndexManager.Core.Provider;

public sealed record QueryUsage(
    string QueryText,
    long ExecutionCount,
    DateTime? LastExecutionUtc,
    UsageSource Source);
