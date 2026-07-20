namespace SmartIndexManager.Core.Provider;

// OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO and SIZE_BASED_CLEANUP_MODE = AUTO
// are fixed defaults applied by querystore-enable.sql. Only the two numbers vary.
public sealed record QueryStoreSettings
{
    public int MaxStorageSizeMb { get; init; } = 1000;
    public int StaleQueryThresholdDays { get; init; } = 30;
}
