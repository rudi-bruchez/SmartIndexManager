using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.ViewModels;

public sealed class IndexRowViewModel : ViewModelBase
{
    public IndexModel Index { get; }
    public ConfidenceScore? ScoreDetail { get; }

    public IndexRowViewModel(
        IndexModel index, ConfidenceScore? score, SafetyAssessment safety,
        bool isRedundant, bool isReferencedByHint)
    {
        Index = index;
        ScoreDetail = score;
        NotDeletable = safety.Eligibility == DeletionEligibility.NotDeletable;
        NotDeletableReason = safety.BlockReason;
        Redundant = isRedundant;
        ReferencedByHint = isReferencedByHint;
        SupportsForeignKey = index.ProviderProperties.ContainsKey("fkSupport");
    }

    public string Database => Index.Database;
    public string Schema => Index.Schema;
    public string Table => Index.Table;
    public string Name => Index.Name;
    public IndexType Type => Index.Type;
    public string KeySummary => string.Join(", ", Index.KeyColumns.Select(c => c.Name));
    public string IncludeSummary => string.Join(", ", Index.IncludedColumns);
    public bool IsUnique => Index.IsUnique;
    public double SizeMb => Index.Size.SizeMb;
    public long Seeks => Index.Usage.Seeks;
    public long Scans => Index.Usage.Scans;
    public long Lookups => Index.Usage.Lookups;
    public long Updates => Index.Usage.Updates;
    public DateTime? LastRead => Index.Usage.LastRead;

    public int? Score => ScoreDetail?.Value;
    public ScoreColor? ScoreColor => ScoreDetail?.Color;

    public bool IsScoreSafe => ScoreColor == Core.Scoring.ScoreColor.Green;
    public bool IsScoreCaution => ScoreColor == Core.Scoring.ScoreColor.Orange;
    public bool IsScoreRisk => ScoreColor == Core.Scoring.ScoreColor.Red;

    public bool NotDeletable { get; }
    public string? NotDeletableReason { get; }
    public bool Redundant { get; }
    public bool ReferencedByHint { get; }
    public bool SupportsForeignKey { get; }
}
