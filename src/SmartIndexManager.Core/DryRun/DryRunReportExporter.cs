using System.Text;
using System.Text.Json;

namespace SmartIndexManager.Core.DryRun;

public static class DryRunReportExporter
{
    public static void ExportJson(string path, DryRunReport report)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }

    public static void ExportMarkdown(string path, DryRunReport report)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var sb = new StringBuilder();
        sb.AppendLine($"# Dry-run report - {report.Server}");
        sb.AppendLine();
        sb.AppendLine($"- Created (UTC): {report.CreatedUtc:O}");
        sb.AppendLine($"- Instance uptime: {report.UptimeDays} days");
        sb.AppendLine($"- Reliability: {report.ReliabilityBadge}");
        sb.AppendLine($"- Estimated space freed: {report.TotalSizeMb:0.0} MB");
        sb.AppendLine($"- Estimated updates avoided: {report.TotalUpdates}");
        sb.AppendLine();
        foreach (var e in report.Entries)
        {
            sb.AppendLine($"## {e.Database}.{e.Schema}.{e.Table}.{e.Index}");
            sb.AppendLine($"- Type: {e.Type}");
            sb.AppendLine($"- Key: {e.Key}");
            sb.AppendLine($"- Includes: {e.Includes}");
            if (!string.IsNullOrEmpty(e.Filter)) sb.AppendLine($"- Filter: {e.Filter}");
            sb.AppendLine($"- Size: {e.SizeMb:0.0} MB");
            sb.AppendLine($"- Score: {e.Score}");
            if (e.SupportsForeignKey) sb.AppendLine("- Supports a foreign key");
            foreach (var w in e.Warnings) sb.AppendLine($"- Warning: {w.Message}");
            foreach (var h in e.Hints) sb.AppendLine($"- Hint: {h.Reference} ({h.Kind})");
            sb.AppendLine();
        }
        File.WriteAllText(path, sb.ToString());
    }
}
