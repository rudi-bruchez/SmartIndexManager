using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.Deletion;

public sealed record DeletionBasketEntry(
    IndexModel Index,
    SafetyAssessment Safety,
    ConfidenceScore? Score);
