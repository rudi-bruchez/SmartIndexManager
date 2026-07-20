using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public class FakesSmokeTests
{
    [Fact]
    public async Task Fake_provider_returns_its_canned_indexes()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered()]
        };
        var factory = new FakeIndexProviderFactory(provider);
        var connected = await factory.ConnectAsync(new ConnectionRequest { Server = "s", Auth = AuthMode.SqlLogin, Login = "u" }, "pw");
        var indexes = await connected.GetIndexesAsync(["Sales"]);
        Assert.Single(indexes);
        Assert.Equal("pw", factory.LastPassword);
    }
}
