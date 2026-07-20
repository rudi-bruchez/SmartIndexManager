namespace SmartIndexManager.Core.Sql;

public sealed record SqlScript(string Name, string Sql, SqlFileHeader Header)
{
    public IReadOnlyList<string> ExpectedColumns => Header.Columns;
}
