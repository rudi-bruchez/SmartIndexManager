using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using Xunit;

namespace SmartIndexManager.App.Tests.ViewModels;

public class QueryStoreStatusViewModelTests
{
    [Fact]
    public async Task Shows_enable_button_when_off_and_alter_granted()
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsQueryStore = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            QueryStore = QueryStoreState.Off
        };
        var vm = new QueryStoreStatusViewModel(new ResxLocalizer());
        vm.SetProvider(provider, "Sales");
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.CanEnable);
    }
}
