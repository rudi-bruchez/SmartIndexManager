using SmartIndexManager.Core.Sql;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Tests.Unit;

// ValidateColumns takes the set of present column names rather than a live SqlDataReader,
// so it is exercisable here without Docker: the internal method is visible to this assembly
// via InternalsVisibleTo.
public sealed class ValidateColumnsTests
{
    private static SqlScript MakeScript(params string[] declaredColumns)
    {
        var header = new SqlFileHeader(
            Name: "test-script",
            MinVersion: new Version(1, 0),
            Azure: AzureSupport.Supported,
            Columns: declaredColumns);
        return new SqlScript("test-script", "SELECT 1;", header);
    }

    [Fact]
    public void MissingDeclaredColumn_Throws()
    {
        var script = MakeScript("A", "B");

        var ex = Assert.Throws<InvalidOperationException>(
            () => SqlClientExecutor.ValidateColumns(script, new[] { "A" }));

        Assert.Contains("B", ex.Message);
    }

    [Fact]
    public void AllDeclaredColumnsPresent_ExactCase_DoesNotThrow()
    {
        var script = MakeScript("A", "B");

        SqlClientExecutor.ValidateColumns(script, new[] { "A", "B" });
    }

    [Fact]
    public void ColumnMatching_IsCaseInsensitive()
    {
        var script = MakeScript("A", "B");

        SqlClientExecutor.ValidateColumns(script, new[] { "a", "b" });
    }
}
