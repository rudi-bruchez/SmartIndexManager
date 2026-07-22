using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Tests.Sql;

public class ScriptContractTests
{
    // Repo-relative path to the shipped scripts, resolved from the test binary location.
    private static string ScriptRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        Assert.NotNull(dir);
        return Path.Combine(dir!, "sql", "sqlserver");
    }

    [Theory]
    [InlineData("server-info", new[] { "ServerName", "ProductVersion", "Edition", "EngineEdition", "UptimeDays" })]
    [InlineData("permissions-check", new[] { "CanViewState", "CanAlter", "CanAccessQueryStore" })]
    [InlineData("querystore-state", new[] { "ActualState" })]
    [InlineData("index-columns", new[] { "ObjectId", "IndexId", "ColumnName", "KeyOrdinal", "IsIncluded", "IsDescending" })]
    [InlineData("fk-support", new[] { "ObjectId", "IndexId" })]
    [InlineData("index-used-by-queries", new[] { "QueryText", "ExecutionCount", "LastExecutionUtc" })]
    [InlineData("index-used-by-queries-query-store", new[] { "QueryText", "ExecutionCount", "LastExecutionUtc" })]
    [InlineData("index-hints-plancache", new[] { "Reference", "Kind" })]
    [InlineData("replication-ag-check", new[] { "InReplicationOrAg" })]
    [InlineData("querystore-enable", new[] { "Applied" })]
    [InlineData("index-droppable-check", new[] { "IsDroppable" })]
    [InlineData("database-exists", new[] { "Exists" })]
    [InlineData("index-exists", new[] { "Exists" })]
    public void Script_ships_and_declares_expected_columns(string name, string[] expected)
    {
        var script = SqlScriptLoader.Load(ScriptRoot(), name);
        foreach (var column in expected)
            Assert.Contains(column, script.ExpectedColumns);
    }

    [Fact]
    public void Index_list_declares_every_column_the_mapper_reads()
    {
        var script = SqlScriptLoader.Load(ScriptRoot(), "index-list");
        foreach (var column in new[]
        {
            "DatabaseName", "SchemaName", "TableName", "IndexName", "IndexTypeCode",
            "IsUnique", "IsPrimaryKey", "IsUniqueConstraint", "IsDisabled", "HasFilter",
            "FilterDefinition", "FillFactor", "DataCompressionCode", "DataSpaceName", "IsPartitioned"
        })
            Assert.Contains(column, script.ExpectedColumns);
    }
}
