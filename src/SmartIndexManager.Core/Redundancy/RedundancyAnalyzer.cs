using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public static class RedundancyAnalyzer
{
    public static IReadOnlyList<RedundancyFinding> Analyze(IEnumerable<IndexModel> indexes)
    {
        var findings = new List<RedundancyFinding>();
        var groups = indexes
            // Disabled indexes serve no query; comparing them would be misleading.
            .Where(i => i.Type == IndexType.NonclusteredRowstore && !i.IsDisabled)
            .GroupBy(i => (i.Database, i.Schema, i.Table),
                     TupleComparer.CaseInsensitive);

        foreach (var group in groups)
        {
            var items = group
                .Select(i => (Index: i, Norm: IndexNormalizer.Normalize(i)))
                .ToList();

            for (int a = 0; a < items.Count; a++)
            for (int b = a + 1; b < items.Count; b++)
            {
                var finding = Compare(items[a].Index, items[a].Norm, items[b].Index, items[b].Norm);
                if (finding is not null) findings.Add(finding);
            }
        }
        return findings;
    }

    private static RedundancyFinding? Compare(IndexModel ia, NormalizedIndex na, IndexModel ib, NormalizedIndex nb)
    {
        // R1: exact duplicate
        if (SameKey(na.Key, nb.Key) && na.Filter == nb.Filter && na.Includes.SetEquals(nb.Includes))
        {
            var (redundant, coveredBy) = ChooseKeeper(ia, ib);
            if (redundant is null) return null; // both unique => never flag
            return new RedundancyFinding(redundant, coveredBy!, RedundancyRule.R1ExactDuplicate);
        }
        return null;
    }

    // Returns (indexToDrop, indexToKeep). Null indexToDrop means neither may be dropped.
    private static (IndexModel?, IndexModel?) ChooseKeeper(IndexModel x, IndexModel y)
    {
        bool xUnique = x.IsUnique, yUnique = y.IsUnique;
        if (xUnique && yUnique) return (null, null);
        if (xUnique) return (y, x);
        if (yUnique) return (x, y);

        // prefer to keep the constraint-backed one, else the more-used one
        bool xConstraint = x.Constraint != ConstraintKind.None;
        bool yConstraint = y.Constraint != ConstraintKind.None;
        if (xConstraint && !yConstraint) return (y, x);
        if (yConstraint && !xConstraint) return (x, y);

        return x.Usage.TotalReads >= y.Usage.TotalReads ? (y, x) : (x, y);
    }

    private static bool SameKey(
        IReadOnlyList<(string Column, SortDirection Direction)> a,
        IReadOnlyList<(string Column, SortDirection Direction)> b)
        => a.Count == b.Count && a.SequenceEqual(b);

    private sealed class TupleComparer : IEqualityComparer<(string Database, string Schema, string Table)>
    {
        public static readonly TupleComparer CaseInsensitive = new();
        public bool Equals((string Database, string Schema, string Table) x, (string Database, string Schema, string Table) y)
            => string.Equals(x.Database, y.Database, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Schema, y.Schema, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string Database, string Schema, string Table) o)
            => HashCode.Combine(
                o.Database.ToLowerInvariant(), o.Schema.ToLowerInvariant(), o.Table.ToLowerInvariant());
    }
}
