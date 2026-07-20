using System.Text;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Ddl;

public static class SqlServerDdlGenerator
{
    public static DdlResult Generate(IndexModel index)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE ");
        if (index.IsUnique) sb.Append("UNIQUE ");
        sb.Append("NONCLUSTERED INDEX ").Append(Quote(index.Name));
        sb.Append(" ON ").Append(Quote(index.Schema)).Append('.').Append(Quote(index.Table));

        sb.Append(" (");
        sb.Append(string.Join(", ", index.KeyColumns.Select(
            c => $"{Quote(c.Name)} {(c.Direction == SortDirection.Ascending ? "ASC" : "DESC")}")));
        sb.Append(')');

        if (index.IncludedColumns.Count > 0)
            sb.Append(" INCLUDE (").Append(string.Join(", ", index.IncludedColumns.Select(Quote))).Append(')');

        if (!string.IsNullOrWhiteSpace(index.FilterPredicate))
            sb.Append(" WHERE ").Append(index.FilterPredicate);

        sb.Append(';');
        return new DdlSuccess(sb.ToString());
    }

    private static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
