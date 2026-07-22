using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.Core.DryRun;

public sealed record DryRunReportEntry
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Index { get; init; }
    public required string Type { get; init; }
    public required string Key { get; init; }
    public required string Includes { get; init; }
    public string? Filter { get; init; }
    public double SizeMb { get; init; }
    public long Seeks { get; init; }
    public long Scans { get; init; }
    public long Lookups { get; init; }
    public long Updates { get; init; }
    public DateTime? LastRead { get; init; }
    public int Score { get; init; }
    public IReadOnlyList<ScoreFactor> ScoreFactors { get; init; } = [];
    public IReadOnlyList<SafetyWarning> Warnings { get; init; } = [];
    public IReadOnlyList<QueryUsage> Queries { get; init; } = [];
    public IReadOnlyList<IndexHint> Hints { get; init; } = [];
    public bool SupportsForeignKey { get; init; }
}
