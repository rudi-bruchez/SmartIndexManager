using System.Text;

namespace SmartIndexManager.Core.Backup;

public static class FileNameSanitizer
{
    // Anything illegal for a filesystem, ambiguous with the '.' separator, or a
    // path separator becomes '_'. The result is intentionally not reversible.
    public static string SanitizeComponent(string component)
    {
        var invalid = new HashSet<char>(Path.GetInvalidFileNameChars()) { '.', '[', ']', '/', '\\', ' ' };
        var sb = new StringBuilder(component.Length);
        foreach (var ch in component)
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }

    public static string BuildIndexBackupFileName(string database, string schema, string table, string index)
        => string.Join('.',
            SanitizeComponent(database),
            SanitizeComponent(schema),
            SanitizeComponent(table),
            SanitizeComponent(index)) + ".sql";
}
