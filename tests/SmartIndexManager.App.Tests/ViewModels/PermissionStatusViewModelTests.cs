using SmartIndexManager.App.Localization;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class PermissionStatusViewModelTests
{
    [Fact]
    public void Missing_view_state_marks_usage_unavailable_with_a_message()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = false, CanAlter = true, CanAccessQueryStore = false });

        Assert.False(vm.UsageAvailable);
        Assert.Contains(vm.Messages, m => m.Contains("Usage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Missing_alter_marks_read_only()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = true, CanAlter = false, CanAccessQueryStore = true });
        Assert.True(vm.ReadOnly);
    }

    [Fact]
    public void All_granted_reports_no_degradation()
    {
        var vm = new PermissionStatusViewModel(new ResxLocalizer());
        vm.Update(new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true });
        Assert.True(vm.UsageAvailable);
        Assert.False(vm.ReadOnly);
        Assert.Empty(vm.Messages);
    }
}
