using System.Text.Json;

namespace SmartIndexManager.Core.Persistence;

public static class ManifestStore
{
    public static void Write(string path, Manifest manifest)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, CoreJson.Options));
    }

    public static Manifest Read(string path)
        => JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), CoreJson.Options)
           ?? throw new InvalidDataException($"manifest at {path} deserialized to null");

    public static Manifest MarkRestored(
        Manifest manifest, string database, string schema, string table, string index, DateTime restoredUtc)
    {
        var updated = manifest.Indexes.Select(e =>
            Matches(e, database, schema, table, index) ? e with { RestoredUtc = restoredUtc } : e).ToList();
        return manifest with { Indexes = updated };
    }

    private static bool Matches(ManifestIndexEntry e, string db, string schema, string table, string index)
        => string.Equals(e.Database, db, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Schema, schema, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Table, table, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.Index, index, StringComparison.OrdinalIgnoreCase);
}
