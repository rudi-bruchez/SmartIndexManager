using System.Text;
using SmartIndexManager.Core.Model;

namespace SmartIndexManager.Core.Backup;

public sealed record BackupHeaderInfo
{
    public required DateTime DateUtc { get; init; }
    public required string Server { get; init; }
    public required string Database { get; init; }
    public required string Operator { get; init; }
    public required string Reason { get; init; }
    public string Comment { get; init; } = "";
    public required IndexUsageStats Stats { get; init; }
}

public static class BackupWriter
{
    public static string WriteIndexBackup(string sessionDir, IndexModel index, string ddl, BackupHeaderInfo header)
    {
        Directory.CreateDirectory(sessionDir);
        var baseName = FileNameSanitizer.BuildIndexBackupFileName(
            index.Database, index.Schema, index.Table, index.Name);
        var fileName = ResolveCollision(sessionDir, baseName);

        var sb = new StringBuilder();
        sb.AppendLine($"-- Date (UTC): {header.DateUtc:O}");
        sb.AppendLine($"-- Server: {header.Server}");
        sb.AppendLine($"-- Database: {header.Database}");
        sb.AppendLine($"-- Index: {index.Schema}.{index.Table}.{index.Name}");
        sb.AppendLine($"-- Operator: {header.Operator}");
        sb.AppendLine($"-- Reason: {header.Reason}");
        if (!string.IsNullOrWhiteSpace(header.Comment))
            sb.AppendLine($"-- Comment: {header.Comment}");
        sb.AppendLine($"-- Stats: seeks={header.Stats.Seeks} scans={header.Stats.Scans} " +
                      $"lookups={header.Stats.Lookups} updates={header.Stats.Updates}");
        sb.AppendLine($"-- LastRead (UTC): {Fmt(header.Stats.LastRead)} " +
                      $"LastWrite (UTC): {Fmt(header.Stats.LastWrite)}");
        sb.AppendLine();
        sb.AppendLine(ddl);

        File.WriteAllText(Path.Combine(sessionDir, fileName), sb.ToString());
        return fileName;   // the manifest 'file' field stores exactly this resolved name
    }

    // Sanitization is not reversible, so two distinct indexes can map to the same
    // base name. A backup .sql is the only recovery artifact, so never overwrite:
    // append " (2)", " (3)", ... before the extension.
    private static string ResolveCollision(string sessionDir, string baseName)
    {
        var path = Path.Combine(sessionDir, baseName);
        if (!File.Exists(path)) return baseName;

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        for (int n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (!File.Exists(Path.Combine(sessionDir, candidate))) return candidate;
        }
    }

    private static string Fmt(DateTime? value) => value?.ToString("O") ?? "never";
}
