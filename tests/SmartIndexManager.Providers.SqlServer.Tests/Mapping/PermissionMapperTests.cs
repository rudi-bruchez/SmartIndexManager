using SmartIndexManager.Providers.SqlServer.Execution;
using SmartIndexManager.Providers.SqlServer.Mapping;

namespace SmartIndexManager.Providers.SqlServer.Tests.Mapping;

public class PermissionMapperTests
{
    private static SqlRow Row(bool view, bool alter, bool qs) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["CanViewState"] = view, ["CanAlter"] = alter, ["CanAccessQueryStore"] = qs
    });

    [Fact]
    public void Maps_flags_and_notes_missing_permissions()
    {
        var report = PermissionMapper.Map(Row(view: false, alter: true, qs: true));
        Assert.False(report.CanViewState);
        Assert.True(report.CanAlter);
        Assert.Contains(report.Notes, n => n.Contains("VIEW", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void All_granted_has_no_notes()
        => Assert.Empty(PermissionMapper.Map(Row(true, true, true)).Notes);
}
