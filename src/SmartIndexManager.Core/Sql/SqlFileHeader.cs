namespace SmartIndexManager.Core.Sql;

public sealed record SqlFileHeader(
    string Name,
    Version MinVersion,
    AzureSupport Azure,
    IReadOnlyList<string> Columns);
