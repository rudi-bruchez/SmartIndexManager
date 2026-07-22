using System.Text;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Ddl;

public static class SqlServerDdlGenerator
{
    public static DdlResult Generate(IndexModel index)
    {
        if (index.Type != IndexType.NonclusteredRowstore)
            return new DdlNotBackupable($"unsupported index type for DDL generation: {index.Type}");
        if (index.IsPartitioned)
            return new DdlNotBackupable("partitioned index DDL cannot be reconstructed with certainty");
        if (index.Options.Compression == DataCompression.Unsupported)
            return new DdlNotBackupable("unsupported data compression option");
        if (index.KeyColumns.Count == 0)
            return new DdlNotBackupable("index has no key columns");

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

        sb.Append(BuildOptions(index.Options));

        if (!string.IsNullOrWhiteSpace(index.DataSpace))
            sb.Append(" ON ").Append(Quote(index.DataSpace));

        sb.Append(';');
        return new DdlSuccess(sb.ToString());
    }

    // Single source of truth for the DROP INDEX statement, shared by the deletion
    // orchestrator's script mode and the provider's live DROP so the two cannot drift.
    public static string GenerateDropStatement(string schema, string table, string index)
        => $"DROP INDEX {Quote(index)} ON {Quote(schema)}.{Quote(table)};";

    private static string BuildOptions(IndexOptions o)
    {
        var parts = new List<string>
        {
            $"PAD_INDEX = {OnOff(o.PadIndex)}"
        };
        if (o.FillFactor is int ff && ff is >= 1 and <= 100) parts.Add($"FILLFACTOR = {ff}");
        parts.Add($"IGNORE_DUP_KEY = {OnOff(o.IgnoreDupKey)}");
        parts.Add($"ALLOW_ROW_LOCKS = {OnOff(o.AllowRowLocks)}");
        parts.Add($"ALLOW_PAGE_LOCKS = {OnOff(o.AllowPageLocks)}");
        parts.Add($"DATA_COMPRESSION = {o.Compression.ToString().ToUpperInvariant()}");
        return " WITH (" + string.Join(", ", parts) + ")";
    }

    private static string OnOff(bool value) => value ? "ON" : "OFF";

    private static string Quote(string identifier) => SqlIdentifier.Quote(identifier);
}
