using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Core.Tests.Sql;

public class SqlScriptLoaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-scripts-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private void WriteScript(string name, string content) => File.WriteAllText(Path.Combine(_dir, $"{name}.sql"), content);

    [Fact]
    public void Load_reads_sql_and_parses_header()
    {
        WriteScript("server-info", """
            -- sim: name=server-info
            -- sim: minversion=11.0
            -- sim: columns=ServerName,Edition
            SELECT 1 AS ServerName, 2 AS Edition;
            """);

        var script = SqlScriptLoader.Load(_dir, "server-info");

        Assert.Equal("server-info", script.Name);
        Assert.Contains("SELECT 1 AS ServerName", script.Sql);
        Assert.Equal(new[] { "ServerName", "Edition" }, script.ExpectedColumns);
    }

    [Fact]
    public void Missing_file_throws_FileNotFoundException_with_the_path()
    {
        var ex = Assert.Throws<FileNotFoundException>(() => SqlScriptLoader.Load(_dir, "does-not-exist"));
        Assert.Contains("does-not-exist.sql", ex.Message);
    }

    [Fact]
    public void Header_name_must_match_the_requested_name()
    {
        WriteScript("server-info", "-- sim: name=other\n-- sim: columns=A\nSELECT 1 AS A;");
        Assert.Throws<SqlFileHeaderException>(() => SqlScriptLoader.Load(_dir, "server-info"));
    }
}
