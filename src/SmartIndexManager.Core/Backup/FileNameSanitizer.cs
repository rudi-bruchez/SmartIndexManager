using System.Text;

namespace SmartIndexManager.Core.Backup;

public static class FileNameSanitizer
{
    private static readonly HashSet<char> InvalidChars = new(Path.GetInvalidFileNameChars())
    {
        '.', '[', ']', '/', '\\', ' '
    };

    // Anything illegal for a filesystem, ambiguous with the '.' separator, or a
    // path separator becomes '_'. The result is intentionally not reversible.
    public static string SanitizeComponent(string component)
    {
        var sb = new StringBuilder(component.Length);
        foreach (var ch in component)
            sb.Append(InvalidChars.Contains(ch) ? '_' : ch);
        return sb.ToString();
    }

    public static string BuildIndexBackupFileName(string database, string schema, string table, string index)
        => string.Join('.',
            SanitizeComponent(database),
            SanitizeComponent(schema),
            SanitizeComponent(table),
            SanitizeComponent(index)) + ".sql";
}
