using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class IndexColumnMapper
{
    public static IndexColumnRow Map(SqlRow row) => new(
        ObjectId: row.Get<long>("ObjectId"),
        IndexId: row.Get<int>("IndexId"),
        ColumnName: row.Get<string>("ColumnName") ?? "",
        KeyOrdinal: row.Get<int>("KeyOrdinal"),
        IsIncluded: row.Get<bool>("IsIncluded"),
        IsDescending: row.Get<bool>("IsDescending"));

    public static IReadOnlyList<IndexColumn> KeyColumns(IEnumerable<IndexColumnRow> rows)
        => rows.Where(r => !r.IsIncluded)
               .OrderBy(r => r.KeyOrdinal)
               .Select(r => new IndexColumn(r.ColumnName,
                   r.IsDescending ? SortDirection.Descending : SortDirection.Ascending))
               .ToList();

    public static IReadOnlyList<string> IncludedColumns(IEnumerable<IndexColumnRow> rows)
        => rows.Where(r => r.IsIncluded)
               .Select(r => r.ColumnName)
               .ToList();
}
