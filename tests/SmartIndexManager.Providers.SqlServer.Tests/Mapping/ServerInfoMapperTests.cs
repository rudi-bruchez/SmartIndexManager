using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class ServerInfoMapperTests
{
    private static SqlRow Row(object engineEdition, object uptime) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["ServerName"] = "PROD01",
        ["ProductVersion"] = "16.0.1000.6",
        ["Edition"] = "Developer Edition (64-bit)",
        ["EngineEdition"] = engineEdition,
        ["UptimeDays"] = uptime
    });

    [Fact]
    public void Maps_version_edition_and_on_premises_platform()
    {
        var info = ServerInfoMapper.Map(Row(3, 92));   // EngineEdition 3 = on-premises
        Assert.Equal("PROD01", info.ServerName);
        Assert.Equal(new Version(16, 0, 1000, 6), info.ProductVersion);
        Assert.Equal(ServerPlatform.OnPremises, info.Platform);
        Assert.Equal(92, info.UptimeDays);
    }

    [Theory]
    [InlineData(5, ServerPlatform.AzureSqlDatabase)]
    [InlineData(8, ServerPlatform.AzureManagedInstance)]
    [InlineData(3, ServerPlatform.OnPremises)]
    public void Maps_engine_edition_to_platform(int engineEdition, ServerPlatform expected)
        => Assert.Equal(expected, ServerInfoMapper.Map(Row(engineEdition, 1)).Platform);

    [Fact]
    public void Null_uptime_becomes_minus_one()
        => Assert.Equal(-1, ServerInfoMapper.Map(Row(5, DBNull.Value)).UptimeDays);
}
