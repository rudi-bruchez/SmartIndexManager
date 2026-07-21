using Material.Icons;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Tests.ViewModels;

public class NavigationDestinationTests
{
    [Fact]
    public void Destination_carries_its_page_and_defaults_to_enabled()
    {
        var page = new PlaceholderPageViewModel("Audit", MaterialIconKind.History, "Planned for a future version.");
        var dest = new NavigationDestination("Audit", MaterialIconKind.History, page);

        Assert.Equal("Audit", dest.Title);
        Assert.Same(page, dest.PageViewModel);
        Assert.True(dest.IsEnabled);
        Assert.Equal("Planned for a future version.", page.Message);
    }
}
