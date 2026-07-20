using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

[Collection("sqlserver")]
public class ConnectAndDetectTests
{
    private readonly SqlServerContainerFixture _fx;
    public ConnectAndDetectTests(SqlServerContainerFixture fx) => _fx = fx;

    [RequiresDockerFact]
    public async Task Connects_and_detects_on_premises_server()
    {
        await using var provider = await _fx.ConnectAsync();

        Assert.Equal(ServerPlatform.OnPremises, provider.ServerInfo.Platform);
        Assert.True(provider.ServerInfo.ProductVersion.Major >= 15);   // container image is 2022
        Assert.True(provider.Permissions.CanViewState);                // sa has full rights
        Assert.True(provider.Capabilities.SupportsQueryStore);
    }
}
