namespace SmartIndexManager.Providers.SqlServer.Mapping;

public sealed record IndexColumnRow(
    long ObjectId, int IndexId, string ColumnName, int KeyOrdinal, bool IsIncluded, bool IsDescending);
