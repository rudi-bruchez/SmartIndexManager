using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Capabilities;

namespace SmartIndexManager.Providers.SqlServer.Tests.Capabilities;

public class CapabilityResolverTests
{
    private static ServerInfo Info(int major, ServerPlatform platform, string edition = "Developer Edition") => new()
    {
        ServerName = "x", ProductVersion = new Version(major, 0), Edition = edition,
        Platform = platform, UptimeDays = 100
    };

    [Fact]
    public void Query_store_needs_2016_on_premises()
    {
        Assert.False(CapabilityResolver.Resolve(Info(12, ServerPlatform.OnPremises)).SupportsQueryStore); // 2014
        Assert.True(CapabilityResolver.Resolve(Info(13, ServerPlatform.OnPremises)).SupportsQueryStore);  // 2016
    }

    [Fact]
    public void Azure_sql_database_always_supports_query_store_and_needs_database_scoped_dmv()
    {
        var caps = CapabilityResolver.Resolve(Info(12, ServerPlatform.AzureSqlDatabase));
        Assert.True(caps.SupportsQueryStore);
        Assert.True(caps.RequiresDatabaseScopedDmv);
    }

    [Fact]
    public void On_premises_does_not_require_database_scoped_dmv()
        => Assert.False(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises)).RequiresDatabaseScopedDmv);

    [Fact]
    public void Online_drop_needs_enterprise_or_azure()
    {
        Assert.False(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises, "Standard Edition")).SupportsOnlineDrop);
        Assert.True(CapabilityResolver.Resolve(Info(15, ServerPlatform.OnPremises, "Enterprise Edition")).SupportsOnlineDrop);
        Assert.True(CapabilityResolver.Resolve(Info(12, ServerPlatform.AzureSqlDatabase)).SupportsOnlineDrop);
    }
}
