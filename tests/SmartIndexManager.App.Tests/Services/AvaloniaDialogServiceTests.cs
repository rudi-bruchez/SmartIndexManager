using SmartIndexManager.App.Services;
using Xunit;

namespace SmartIndexManager.App.Tests.Services;

public class AvaloniaDialogServiceTests
{
    [Fact]
    public async Task RequestAsync_returns_null_when_no_desktop_lifetime()
    {
        var service = new AvaloniaDialogService();

        var result = await service.RequestAsync("test-server", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void Service_implements_IPasswordPrompt_and_IDialogService()
    {
        var service = new AvaloniaDialogService();

        Assert.IsAssignableFrom<IPasswordPrompt>(service);
        Assert.IsAssignableFrom<IDialogService>(service);
    }
}
