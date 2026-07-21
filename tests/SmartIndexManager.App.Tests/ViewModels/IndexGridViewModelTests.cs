using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class IndexGridViewModelTests
{
    private static IndexRowViewModel Row(string db, string table, string name)
        => new(IndexModelFactory.Nonclustered(db: db, table: table, name: name),
               new ConfidenceScore(50, ScoreColor.Orange, []),
               new SafetyAssessment(DeletionEligibility.Deletable, null, []),
               isRedundant: false, isReferencedByHint: false);

    [Fact]
    public void SetRows_populates_the_view()
    {
        var vm = new IndexGridViewModel();
        vm.SetRows([Row("Sales", "Orders", "IX_A"), Row("HR", "Staff", "IX_B")]);
        Assert.Equal(2, vm.VisibleCount);
    }

    [Fact]
    public void FilterText_narrows_the_view_by_substring_across_columns()
    {
        var vm = new IndexGridViewModel();
        vm.SetRows([Row("Sales", "Orders", "IX_Orders_Customer"), Row("HR", "Staff", "IX_Staff_Name")]);

        vm.FilterText = "orders";
        Assert.Equal(1, vm.VisibleCount);

        vm.FilterText = "";
        Assert.Equal(2, vm.VisibleCount);
    }

    [Fact]
    public void MatchCountText_reflects_filter_and_total()
    {
        var vm = new IndexGridViewModel();   // no localizer -> "V of T" fallback
        IndexRowViewModel Row(string name) => new(
            SmartIndexManager.App.Tests.Fakes.IndexModelFactory.Nonclustered(name: name),
            null, new SmartIndexManager.Core.Safety.SafetyAssessment(SmartIndexManager.Core.Safety.DeletionEligibility.Deletable, null, []), false, false);

        vm.SetRows([Row("AAA"), Row("BBB"), Row("HR_legacy")]);
        Assert.Equal(3, vm.TotalCount);
        Assert.Equal("3 of 3", vm.MatchCountText);

        vm.FilterText = "HR";
        Assert.Equal("1 of 3", vm.MatchCountText);

        vm.FilterText = "";
        Assert.Equal("3 of 3", vm.MatchCountText);
    }

    [Fact]
    public void Filter_flags_and_clear_command_reset_filter()
    {
        var vm = new IndexGridViewModel();
        IndexRowViewModel Row(string name) => new(
            SmartIndexManager.App.Tests.Fakes.IndexModelFactory.Nonclustered(name: name),
            null, new SmartIndexManager.Core.Safety.SafetyAssessment(SmartIndexManager.Core.Safety.DeletionEligibility.Deletable, null, []), false, false);

        vm.SetRows([Row("AAA"), Row("BBB"), Row("HR_legacy")]);
        Assert.False(vm.IsFiltered);
        Assert.True(vm.HasVisibleRows);

        vm.FilterText = "ZZZ";
        Assert.True(vm.IsFiltered);
        Assert.False(vm.HasVisibleRows);

        vm.ClearFilterCommand.Execute(null);
        Assert.Equal("", vm.FilterText);
        Assert.False(vm.IsFiltered);
        Assert.True(vm.HasVisibleRows);
    }
}
