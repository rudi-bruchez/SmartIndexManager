using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class QueryUsageMapper
{
    public static QueryUsage Map(SqlRow row, UsageSource source) => new(
        QueryText: row.Get<string>("QueryText") ?? "",
        ExecutionCount: row.Get<long>("ExecutionCount"),
        LastExecutionUtc: row.GetRaw("LastExecutionUtc") is null ? null : row.Get<DateTime>("LastExecutionUtc"),
        Source: source);
}
