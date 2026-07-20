using SmartIndexManager.Core.Ddl;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Safety;

public static class DeletionSafetyEvaluator
{
    public static SafetyAssessment Evaluate(SafetyInputs inputs)
    {
        var index = inputs.Index;

        string? block = HardExclusion(index)
            ?? (inputs.Ddl is DdlNotBackupable nb ? $"DDL non sauvegardable: {nb.Reason}" : null);

        if (block is not null)
            return new SafetyAssessment(DeletionEligibility.NotDeletable, block, []);

        var warnings = new List<SafetyWarning>();
        if (inputs.SupportsForeignKey)
            warnings.Add(new SafetyWarning("fk-support", "This index supports a foreign key."));
        if (index.FilterPredicate is not null)
            warnings.Add(new SafetyWarning("filtered", "Filtered index."));
        if (inputs.ReferencedByHint)
            warnings.Add(new SafetyWarning("hint", "Referenced by a hint or plan guide; queries may fail."));
        if (inputs.DatabaseInReplicationOrAg)
            warnings.Add(new SafetyWarning("replication-ag", "Database is in replication or an availability group."));
        if (inputs.InstanceUptimeDays < inputs.UptimeReliabilityThresholdDays)
            warnings.Add(new SafetyWarning("short-uptime",
                $"Instance uptime {inputs.InstanceUptimeDays}d below reliability threshold."));

        return new SafetyAssessment(DeletionEligibility.Deletable, null, warnings);
    }

    private static string? HardExclusion(IndexModel index)
    {
        if (index.Type != IndexType.NonclusteredRowstore)
            return $"index type {index.Type} is never deletable";
        if (index.IsUnique || index.Constraint != ConstraintKind.None)
            return "unique or constraint-backed index";
        if (index.IsDisabled) return "disabled index";
        if (index.IsOnView) return "index on a view";
        if (index.IsOnSystemTable) return "index on a system table";
        return null;
    }
}
