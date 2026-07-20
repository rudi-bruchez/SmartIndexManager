using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class HintMapper
{
    public static IndexHint Map(SqlRow row)
        => new(row.Get<string>("Reference") ?? "", row.Get<string>("Kind") ?? "query hint");
}
