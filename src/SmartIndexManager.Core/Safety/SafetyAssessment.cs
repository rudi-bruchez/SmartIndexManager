namespace SmartIndexManager.Core.Safety;

public enum DeletionEligibility { Deletable, NotDeletable }

public sealed record SafetyAssessment(
    DeletionEligibility Eligibility,
    string? BlockReason,
    IReadOnlyList<SafetyWarning> Warnings);
