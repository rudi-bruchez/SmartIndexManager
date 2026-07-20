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
}
