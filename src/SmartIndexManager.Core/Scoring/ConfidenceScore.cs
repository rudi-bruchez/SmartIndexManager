namespace SmartIndexManager.Core.Scoring;

public sealed record ConfidenceScore(int Value, ScoreColor Color, IReadOnlyList<ScoreFactor> Factors);
