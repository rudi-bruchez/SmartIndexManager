using SmartIndexManager.Core.Sql;
using Xunit;

namespace SmartIndexManager.Core.Tests.Sql;

public class SqlFileHeaderParserTests
{
    private const string Valid = """
        -- sim: name=unused-indexes
        -- sim: minversion=11.0
        -- sim: azure=supported
        -- sim: columns=SchemaName,TableName,IndexName,UserSeeks
        SELECT 1;
        """;

    [Fact]
    public void Parses_a_valid_header()
    {
        var header = SqlFileHeaderParser.Parse(Valid);
        Assert.Equal("unused-indexes", header.Name);
        Assert.Equal(new Version(11, 0), header.MinVersion);
        Assert.Equal(AzureSupport.Supported, header.Azure);
        Assert.Equal(new[] { "SchemaName", "TableName", "IndexName", "UserSeeks" }, header.Columns);
    }

    [Fact]
    public void Azure_defaults_to_supported_when_absent()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\n-- sim: columns=A\nSELECT 1;";
        Assert.Equal(AzureSupport.Supported, SqlFileHeaderParser.Parse(content).Azure);
    }

    [Fact]
    public void Missing_name_throws()
    {
        var content = "-- sim: minversion=11.0\n-- sim: columns=A\nSELECT 1;";
        var ex = Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Missing_columns_throws()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\nSELECT 1;";
        Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
    }

    [Fact]
    public void Malformed_version_throws()
    {
        var content = "-- sim: name=x\n-- sim: minversion=abc\n-- sim: columns=A\nSELECT 1;";
        Assert.Throws<SqlFileHeaderException>(() => SqlFileHeaderParser.Parse(content));
    }

    [Fact]
    public void Azure_unsupported_parses()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\n-- sim: azure=unsupported\n-- sim: columns=A\nSELECT 1;";
        Assert.Equal(AzureSupport.Unsupported, SqlFileHeaderParser.Parse(content).Azure);
    }

    [Fact]
    public void Azure_only_parses()
    {
        var content = "-- sim: name=x\n-- sim: minversion=11.0\n-- sim: azure=only\n-- sim: columns=A\nSELECT 1;";
        Assert.Equal(AzureSupport.Only, SqlFileHeaderParser.Parse(content).Azure);
    }

    [Fact]
    public void Stops_scanning_at_first_non_metadata_line()
    {
        var content = """
            -- sim: name=x
            -- sim: minversion=11.0
            -- sim: azure=unsupported
            -- sim: columns=A
            SELECT 1;
            -- sim: name=y
            -- sim: columns=B
            """;
        var header = SqlFileHeaderParser.Parse(content);
        Assert.Equal("x", header.Name);
        Assert.Equal(AzureSupport.Unsupported, header.Azure);
        Assert.Equal(new[] { "A" }, header.Columns);
    }

    [Fact]
    public void Tolerates_ordinary_comments_before_metadata_block()
    {
        var content = """
            -- regular license header
            -- another comment
            -- sim: name=x
            -- sim: minversion=11.0
            -- sim: columns=A
            SELECT 1;
            """;
        var header = SqlFileHeaderParser.Parse(content);
        Assert.Equal("x", header.Name);
        Assert.Equal(new[] { "A" }, header.Columns);
    }
}
