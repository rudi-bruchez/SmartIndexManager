using SmartIndexManager.Core.Model;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class IndexRowMapper
{
    public static IndexModel Map(SqlRow row, IReadOnlyList<IndexColumnRow> columns)
    {
        bool isPrimaryKey = row.Get<bool>("IsPrimaryKey");
        bool isUniqueConstraint = row.Get<bool>("IsUniqueConstraint");
        int fillFactor = row.Get<int>("FillFactor");

        return new IndexModel
        {
            Database = row.Get<string>("DatabaseName") ?? "",
            Schema = row.Get<string>("SchemaName") ?? "",
            Table = row.Get<string>("TableName") ?? "",
            Name = row.Get<string>("IndexName") ?? "",
            Type = MapType(row.Get<int>("IndexTypeCode")),
            KeyColumns = IndexColumnMapper.KeyColumns(columns),
            IncludedColumns = IndexColumnMapper.IncludedColumns(columns),
            FilterPredicate = row.Get<bool>("HasFilter") ? row.Get<string>("FilterDefinition") : null,
            IsUnique = row.Get<bool>("IsUnique"),
            Constraint = isPrimaryKey ? ConstraintKind.PrimaryKey
                       : isUniqueConstraint ? ConstraintKind.Unique
                       : ConstraintKind.None,
            IsDisabled = row.Get<bool>("IsDisabled"),
            IsOnView = row.Get<bool>("IsOnView"),
            IsOnSystemTable = row.Get<bool>("IsSystemObject"),
            IsPartitioned = row.Get<bool>("IsPartitioned"),
            DataSpace = row.Get<string>("DataSpaceName"),
            Options = new IndexOptions
            {
                FillFactor = fillFactor is >= 1 and <= 100 ? fillFactor : null,
                PadIndex = row.Get<bool>("IsPadded"),
                AllowRowLocks = row.Get<bool>("AllowRowLocks"),
                AllowPageLocks = row.Get<bool>("AllowPageLocks"),
                IgnoreDupKey = row.Get<bool>("IgnoreDupKey"),
                Compression = MapCompression(row.Get<int>("DataCompressionCode"))
            }
        };
    }

    private static IndexType MapType(int code) => code switch
    {
        0 => IndexType.Heap,
        1 => IndexType.ClusteredRowstore,
        2 => IndexType.NonclusteredRowstore,
        3 => IndexType.Xml,
        4 => IndexType.Spatial,
        5 => IndexType.ClusteredColumnstore,
        6 => IndexType.NonclusteredColumnstore,
        7 => IndexType.Spatial,          // 7 = extended/spatial family; treated as non-droppable
        _ => IndexType.Hypothetical
    };

    private static DataCompression MapCompression(int code) => code switch
    {
        0 => DataCompression.None,
        1 => DataCompression.Row,
        2 => DataCompression.Page,
        _ => DataCompression.Unsupported     // 3/4 = columnstore compression, not reconstructable here
    };
}
