using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using Xunit;

namespace SmartIndexManager.Core.Tests.Safety;

public class DeletionSafetyEvaluatorTests
{
    private static IndexModel Deletable() => new()
    {
        Database = "db", Schema = "dbo", Table = "Orders", Name = "IX",
        Type = IndexType.NonclusteredRowstore,
        KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
    };

    private static SafetyInputs Inputs(IndexModel index, DdlResult? ddl = null) => new()
    {
        Index = index,
        Ddl = ddl ?? new DdlSuccess("CREATE NONCLUSTERED INDEX ..."),
        InstanceUptimeDays = 90
    };

    [Fact]
    public void Plain_nonclustered_rowstore_is_deletable()
    {
        var a = DeletionSafetyEvaluator.Evaluate(Inputs(Deletable()));
        Assert.Equal(DeletionEligibility.Deletable, a.Eligibility);
        Assert.Null(a.BlockReason);
        Assert.Empty(a.Warnings);
    }

    [Theory]
    [InlineData(ConstraintKind.PrimaryKey)]
    [InlineData(ConstraintKind.Unique)]
    public void Constraint_backed_indexes_are_not_deletable(ConstraintKind kind)
    {
        var index = Deletable() with { Constraint = kind, IsUnique = kind == ConstraintKind.Unique };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Fact]
    public void Unique_without_constraint_is_not_deletable()
    {
        var index = Deletable() with { IsUnique = true };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Theory]
    [InlineData(IndexType.ClusteredRowstore)]
    [InlineData(IndexType.NonclusteredColumnstore)]
    [InlineData(IndexType.Xml)]
    [InlineData(IndexType.Spatial)]
    [InlineData(IndexType.Hypothetical)]
    public void Excluded_types_are_not_deletable(IndexType type)
    {
        var index = Deletable() with { Type = type };
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(index)).Eligibility);
    }

    [Fact]
    public void Disabled_view_and_system_indexes_are_not_deletable()
    {
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsDisabled = true })).Eligibility);
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsOnView = true })).Eligibility);
        Assert.Equal(DeletionEligibility.NotDeletable,
            DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { IsOnSystemTable = true })).Eligibility);
    }

    [Fact]
    public void Not_backupable_ddl_blocks_deletion()
    {
        var a = DeletionSafetyEvaluator.Evaluate(
            Inputs(Deletable(), new DdlNotBackupable("partitioned")));
        Assert.Equal(DeletionEligibility.NotDeletable, a.Eligibility);
        Assert.Contains("DDL non sauvegardable", a.BlockReason);
    }

    [Fact]
    public void Guard_rails_surface_as_warnings_but_stay_deletable()
    {
        var a = DeletionSafetyEvaluator.Evaluate(new SafetyInputs
        {
            Index = Deletable() with { FilterPredicate = "Status = 1" },
            Ddl = new DdlSuccess("..."),
            SupportsForeignKey = true,
            ReferencedByHint = true,
            DatabaseInReplicationOrAg = true,
            InstanceUptimeDays = 5
        });

        Assert.Equal(DeletionEligibility.Deletable, a.Eligibility);
        var codes = a.Warnings.Select(w => w.Code).ToHashSet();
        Assert.Contains("fk-support", codes);
        Assert.Contains("filtered", codes);
        Assert.Contains("hint", codes);
        Assert.Contains("replication-ag", codes);
        Assert.Contains("short-uptime", codes);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void Whitespace_filter_predicate_does_not_warn(string? filter)
    {
        var a = DeletionSafetyEvaluator.Evaluate(Inputs(Deletable() with { FilterPredicate = filter }));
        Assert.Equal(DeletionEligibility.Deletable, a.Eligibility);
        Assert.DoesNotContain(a.Warnings, w => w.Code == "filtered");
    }
}
