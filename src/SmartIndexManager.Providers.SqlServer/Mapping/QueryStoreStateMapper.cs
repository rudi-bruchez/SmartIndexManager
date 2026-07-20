using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class QueryStoreStateMapper
{
    // sys.database_query_store_options.actual_state: 0 OFF, 1 READ_ONLY, 2 READ_WRITE, 3 ERROR.
    // Null means the row is absent (Query Store never configured) -> treated as OFF.
    public static QueryStoreState Map(int? actualState) => actualState switch
    {
        1 => QueryStoreState.ReadOnly,
        2 => QueryStoreState.ReadWrite,
        _ => QueryStoreState.Off
    };
}
