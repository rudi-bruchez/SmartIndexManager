using SmartIndexManager.Core.Backup;
using Xunit;

namespace SmartIndexManager.Core.Tests.Backup;

public class FileNameSanitizerTests
{
    [Fact]
    public void Clean_components_keep_the_dot_separator()
    {
        var name = FileNameSanitizer.BuildIndexBackupFileName("Sales", "dbo", "Orders", "IX_Orders_Legacy");
        Assert.Equal("Sales.dbo.Orders.IX_Orders_Legacy.sql", name);
    }

    [Fact]
    public void Dots_inside_a_component_are_sanitized_but_separators_remain()
    {
        var name = FileNameSanitizer.BuildIndexBackupFileName("Sales", "dbo", "Orders.2024", "IX_Legacy");
        Assert.Equal("Sales.dbo.Orders_2024.IX_Legacy.sql", name);
    }

    [Theory]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a b", "a_b")]
    [InlineData("[weird]", "_weird_")]
    public void Illegal_characters_become_underscore(string input, string expected)
        => Assert.Equal(expected, FileNameSanitizer.SanitizeComponent(input));
}
