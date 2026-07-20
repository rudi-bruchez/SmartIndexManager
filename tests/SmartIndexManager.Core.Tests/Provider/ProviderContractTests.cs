using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.Core.Tests.Provider;

public class ProviderContractTests
{
    [Fact]
    public void IndexRef_Of_copies_the_four_identity_parts()
    {
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_Legacy",
            Type = IndexType.NonclusteredRowstore
        };
        Assert.Equal(new IndexRef("Sales", "dbo", "Orders", "IX_Legacy"), IndexRef.Of(index));
    }

    [Fact]
    public void QueryStoreSettings_defaults_match_the_spec()
    {
        var s = new QueryStoreSettings();
        Assert.Equal(1000, s.MaxStorageSizeMb);
        Assert.Equal(30, s.StaleQueryThresholdDays);
    }

    [Fact]
    public void ServerInfo_unknown_uptime_is_minus_one()
    {
        var info = new ServerInfo
        {
            ServerName = "x", ProductVersion = new Version(16, 0), Edition = "Developer",
            Platform = ServerPlatform.AzureSqlDatabase, UptimeDays = -1
        };
        Assert.Equal(-1, info.UptimeDays);
    }
}
