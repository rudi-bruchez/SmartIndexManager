using System.Text;
using SmartIndexManager.Core.IO;
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
        var content = BuildContent(index, ddl, header);
        var baseName = FileNameSanitizer.BuildIndexBackupFileName(
            index.Database, index.Schema, index.Table, index.Name);

        var stem = Path.GetFileNameWithoutExtension(baseName);
        var ext = Path.GetExtension(baseName);
        for (int n = 1; ; n++)
        {
            var candidate = n == 1 ? baseName : $"{stem} ({n}){ext}";
            var path = Path.Combine(sessionDir, candidate);
            if (AtomicFile.TryWriteAllText(path, content))
                return candidate;   // the manifest 'file' field stores exactly this resolved name
        }
    }

    private static string BuildContent(IndexModel index, string ddl, BackupHeaderInfo header)
    {
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
        return sb.ToString();
    }

    private static string Fmt(DateTime? value) => value?.ToString("O") ?? "never";
}
