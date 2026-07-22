namespace SmartIndexManager.Core.Ddl;

// Single source of truth for bracket-quoting SQL Server identifiers (]] escaped).
// Identifiers can never be parameterized, so every place that builds SQL text with an
// identifier goes through here rather than re-implementing the escape.
public static class SqlIdentifier
{
    public static string Quote(string identifier) => $"[{identifier.Replace("]", "]]")}]";
}
