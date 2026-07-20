using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public sealed record NormalizedIndex(
    IReadOnlyList<(string Column, SortDirection Direction)> Key,
    IReadOnlySet<string> Includes,
    string? Filter,
    bool IsUnique);
