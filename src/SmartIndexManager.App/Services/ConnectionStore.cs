using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.App.Services;

public sealed class ConnectionStore : IConnectionStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _path;

    public ConnectionStore(IAppPaths paths)
        => _path = Path.Combine(paths.ConfigDir, "connections.json");

    public IReadOnlyList<ConnectionProfile> Load()
    {
        if (!File.Exists(_path)) return [];
        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, Options) ?? [];
    }

    public void Save(IReadOnlyList<ConnectionProfile> profiles)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(profiles, Options));
    }
}
