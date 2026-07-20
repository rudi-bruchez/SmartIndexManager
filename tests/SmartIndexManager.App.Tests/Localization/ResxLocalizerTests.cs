using SmartIndexManager.App.Localization;

namespace SmartIndexManager.App.Tests.Localization;

public class ResxLocalizerTests
{
    [Fact]
    public void Returns_the_english_string_for_a_known_key()
    {
        ILocalizer loc = new ResxLocalizer();
        Assert.Equal("Connect", loc["Action_Connect"]);
    }

    [Fact]
    public void Unknown_key_returns_the_key_in_brackets_so_gaps_are_visible()
    {
        ILocalizer loc = new ResxLocalizer();
        Assert.Equal("[Missing_Key]", loc["Missing_Key"]);
    }
}
