using Microsoft.Extensions.DependencyInjection;
using SmartIndexManager.App.Composition;
using SmartIndexManager.Core.Provider;

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
}
