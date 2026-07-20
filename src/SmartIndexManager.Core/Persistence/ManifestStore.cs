using System.Text.Json;
using SmartIndexManager.Core.IO;

namespace SmartIndexManager.Core.Persistence;

public static class ManifestStore
{
    public static void Write(string path, Manifest manifest)
        => AtomicFile.WriteAllText(path, JsonSerializer.Serialize(manifest, CoreJson.Options));

    public static Manifest Read(string path)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), CoreJson.Options)
            ?? throw new InvalidDataException($"manifest at {path} deserialized to null");
        ValidateSchemaVersion(manifest, path);
        return manifest;
    }

    private static void ValidateSchemaVersion(Manifest manifest, string path)
    {
        if (manifest.SchemaVersion != Manifest.CurrentSchemaVersion)
            throw new InvalidDataException(
                $"manifest at {path} has unsupported schema version {manifest.SchemaVersion}; expected {Manifest.CurrentSchemaVersion}");
    }

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
