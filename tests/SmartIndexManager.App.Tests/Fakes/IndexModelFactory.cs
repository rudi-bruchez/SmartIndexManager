using SmartIndexManager.Core.Model;

namespace SmartIndexManager.App.Tests.Fakes;

// Builders for IndexModel test data. Defaults describe a plain deletable nonclustered rowstore index.
public static class IndexModelFactory
{
    public static IndexModel Nonclustered(
        string db = "Sales", string schema = "dbo", string table = "Orders", string name = "IX_Orders_Customer",
        IReadOnlyList<string>? keyColumns = null, IReadOnlyList<string>? includedColumns = null,
        bool isUnique = false, ConstraintKind constraint = ConstraintKind.None,
        long seeks = 0, long scans = 0, long lookups = 0, long updates = 0, DateTime? lastRead = null)
        => new()
        {
            Database = db, Schema = schema, Table = table, Name = name,
            Type = IndexType.NonclusteredRowstore,
            KeyColumns = (keyColumns ?? ["CustomerId"]).Select(c => new IndexColumn(c, SortDirection.Ascending)).ToList(),
            IncludedColumns = includedColumns ?? [],
            IsUnique = isUnique,
            Constraint = constraint,
            Usage = new IndexUsageStats(seeks, scans, lookups, updates, lastRead, null),
            Size = new IndexSizeInfo(Pages: 100, Rows: 1000, SizeMb: 8.0),
            Options = new IndexOptions()
        };

    public static IndexModel PrimaryKey(string name = "PK_Orders")
        => Nonclustered(name: name, isUnique: true, constraint: ConstraintKind.PrimaryKey) with
           { Type = IndexType.ClusteredRowstore };
}
