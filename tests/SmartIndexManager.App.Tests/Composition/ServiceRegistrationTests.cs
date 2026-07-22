using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Restore;

namespace SmartIndexManager.App.Tests.Composition;

public class ServiceRegistrationTests
{
    [Fact]
    public void AddAppServices_registers_the_provider_factory()
    {
        var provider = new ServiceCollection()
            .AddAppServices(scriptRoot: "/tmp/sql/sqlserver")
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<IIndexProviderFactory>());
    }

    [Fact]
    public void AddAppServices_registers_new_3b_services()
    {
        var provider = new ServiceCollection()
            .AddAppServices(scriptRoot: "/tmp/sql/sqlserver")
            .BuildServiceProvider();

        Assert.NotNull(provider.GetService<DeletionBasket>());
        Assert.NotNull(provider.GetService<DeletionOrchestrator>());
        Assert.NotNull(provider.GetService<RestoreService>());
        Assert.NotNull(provider.GetService<IPasswordPrompt>());
    }
}
