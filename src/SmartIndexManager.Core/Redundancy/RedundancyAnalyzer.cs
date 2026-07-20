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
            if (redundant is null) return null;
            return new RedundancyFinding(redundant, coveredBy!, RedundancyRule.R1ExactDuplicate);
        }

        // R2: one key is a strict prefix of the other, same filter, shorter's includes covered
        if (na.Filter == nb.Filter)
        {
            var r2 = TryCoveredPrefix(ia, na, ib, nb) ?? TryCoveredPrefix(ib, nb, ia, na);
            if (r2 is not null) return r2;
        }

        // R3: same key, same filter, one includes strictly dominates the other
        if (SameKey(na.Key, nb.Key) && na.Filter == nb.Filter)
        {
            if (!ia.IsUnique && na.Includes.IsProperSubsetOf(nb.Includes))
                return new RedundancyFinding(ia, ib, RedundancyRule.R3DominatedIncludes);
            if (!ib.IsUnique && nb.Includes.IsProperSubsetOf(na.Includes))
                return new RedundancyFinding(ib, ia, RedundancyRule.R3DominatedIncludes);
        }

        return null;
    }

    // Is `shorter` redundant against `longer`? shorter.Key strict prefix of longer.Key
    // (same directions on the prefix) and shorter.Includes subset of (longer.Key columns
    // beyond the prefix union longer.Includes). Only `shorter` is ever flagged, and only
    // when it is non-unique; `longer` may be unique (we keep it, which is correct). The UI
    // wording should make clear the covering index is the one being kept, not dropped.
    private static RedundancyFinding? TryCoveredPrefix(
        IndexModel shorter, NormalizedIndex ns, IndexModel longer, NormalizedIndex nl)
    {
        if (shorter.IsUnique) return null;
        if (ns.Key.Count >= nl.Key.Count) return null;
        for (int i = 0; i < ns.Key.Count; i++)
            if (ns.Key[i] != nl.Key[i]) return null;

        var covered = new HashSet<string>(StringComparer.Ordinal);
        for (int i = ns.Key.Count; i < nl.Key.Count; i++) covered.Add(nl.Key[i].Column);
        foreach (var inc in nl.Includes) covered.Add(inc);

        if (!ns.Includes.IsSubsetOf(covered)) return null;
        return new RedundancyFinding(shorter, longer, RedundancyRule.R2CoveredPrefix);
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
