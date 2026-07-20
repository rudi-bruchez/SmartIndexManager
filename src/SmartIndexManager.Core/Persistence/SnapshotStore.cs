using System.Text.Json;
using SmartIndexManager.Core.Backup;

namespace SmartIndexManager.Core.Persistence;

public static class SnapshotStore
{
    public static string Write(string rootDir, UsageSnapshot snapshot)
    {
        var dir = DirFor(rootDir, snapshot.Server, snapshot.Database);
        Directory.CreateDirectory(dir);
        var fileName = snapshot.CapturedUtc.ToUniversalTime().ToString("yyyyMMddTHHmmssZ") + ".json";
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, CoreJson.Options));
        return path;
    }

    public static IReadOnlyList<UsageSnapshot> ReadAll(string rootDir, string server, string database)
    {
        var dir = DirFor(rootDir, server, database);
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(DeserializeOrNull)
            .Where(s => s is not null)
            .Select(s => s!)
            .OrderBy(s => s.CapturedUtc)
            .ToList();
    }

    private static UsageSnapshot? DeserializeOrNull(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<UsageSnapshot>(File.ReadAllText(path), CoreJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static DateTime? OldestCaptureUtc(string rootDir, string server, string database)
    {
        var all = ReadAll(rootDir, server, database);
        return all.Count == 0 ? null : all[0].CapturedUtc;
    }

    public static int PurgeOlderThan(string rootDir, string server, string database, DateTime cutoffUtc)
    {
        var dir = DirFor(rootDir, server, database);
        if (!Directory.Exists(dir)) return 0;
        int purged = 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").ToList())
        {
            var snap = DeserializeOrNull(path);
            if (snap is not null && snap.CapturedUtc < cutoffUtc)
            {
                File.Delete(path);
                purged++;
            }
        }
        return purged;
    }

    private static string DirFor(string rootDir, string server, string database)
        => Path.Combine(rootDir, "snapshots",
            FileNameSanitizer.SanitizeComponent(server),
            FileNameSanitizer.SanitizeComponent(database));
}
