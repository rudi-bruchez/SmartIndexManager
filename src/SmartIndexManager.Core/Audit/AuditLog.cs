using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.Core.Audit;

public static class AuditLog
{
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static void Append(string logFilePath, AuditEntry entry)
    {
        var dir = Path.GetDirectoryName(logFilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var line = JsonSerializer.Serialize(entry, LineOptions);
        File.AppendAllText(logFilePath, line + Environment.NewLine, Encoding.UTF8);
    }

    // A corrupt or truncated line must not stop the rest of the log from being read.
    public static IReadOnlyList<AuditEntry> Read(string logFilePath)
    {
        if (!File.Exists(logFilePath)) return [];
        var entries = new List<AuditEntry>();
        foreach (var line in File.ReadAllLines(logFilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            AuditEntry? entry;
            try { entry = JsonSerializer.Deserialize<AuditEntry>(line, LineOptions); }
            catch (JsonException) { continue; }
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }
}
