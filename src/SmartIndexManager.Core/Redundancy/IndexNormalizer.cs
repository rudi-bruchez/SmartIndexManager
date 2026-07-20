using System.Text;
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

    // Syntactic normalization only: lower-case identifiers and keywords, collapse
    // whitespace, strip fully-enclosing redundant parentheses. String literals are
    // preserved (including their case) because two predicates WHERE Region = 'US' and
    // WHERE Region = 'us' filter different rows and must not be reported as identical.
    // Brackets are deliberately NOT stripped for the same reason (e.g. LIKE '[0-9]%').
    public static string? NormalizeFilter(string? predicate)
    {
        if (string.IsNullOrWhiteSpace(predicate)) return null;
        var s = LowerOutsideStringLiterals(predicate);
        s = Whitespace().Replace(s, " ").Trim();
        s = StripEnclosingParens(s);
        return s;
    }

    private static string LowerOutsideStringLiterals(string s)
    {
        var sb = new StringBuilder(s.Length);
        bool inString = false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'')
            {
                // SQL escaped quote ('') stays inside the literal.
                if (inString && i + 1 < s.Length && s[i + 1] == '\'')
                {
                    sb.Append("''");
                    i++;
                    continue;
                }
                inString = !inString;
                sb.Append(c);
                continue;
            }
            sb.Append(inString ? c : char.ToLowerInvariant(c));
        }
        return sb.ToString();
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
