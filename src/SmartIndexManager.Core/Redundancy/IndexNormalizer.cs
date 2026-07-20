using System.Text.RegularExpressions;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Redundancy;

public static partial class IndexNormalizer
{
    public static NormalizedIndex Normalize(IndexModel index)
    {
        var key = index.KeyColumns
            .Select(c => (Column: c.Name.ToLowerInvariant(), c.Direction))
            .ToList();
        var includes = index.IncludedColumns
            .Select(c => c.ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        return new NormalizedIndex(key, includes, NormalizeFilter(index.FilterPredicate), index.IsUnique);
    }

    // Syntactic normalization only: lower-case, collapse whitespace, strip
    // fully-enclosing redundant parentheses. Brackets are deliberately NOT stripped:
    // removing them could unify two genuinely different predicates (for example a
    // LIKE '[0-9]%' character class), which would be a false positive. The invariant
    // is "fewer redundancies reported, never a false one", so bracketed and
    // unbracketed identifiers stay distinct (a safe false negative).
    public static string? NormalizeFilter(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate)) return null;
        var s = predicate.ToLowerInvariant();
        s = Whitespace().Replace(s, " ").Trim();
        s = StripEnclosingParens(s);
        return s;
    }

    private static string StripEnclosingParens(string s)
    {
        while (s.Length >= 2 && s[0] == '(' && s[^1] == ')' && ParensBalancedInside(s))
            s = s[1..^1].Trim();
        return s;
    }

    private static bool ParensBalancedInside(string s)
    {
        int depth = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            if (depth == 0 && i < s.Length - 1) return false; // closes before the end
        }
        return depth == 0;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
