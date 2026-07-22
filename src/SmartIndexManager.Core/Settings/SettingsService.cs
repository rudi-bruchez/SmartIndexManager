using System.Text.Json;

namespace SmartIndexManager.Core.Settings;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public AppSettings Load(string configDir)
    {
        var path = Path.Combine(configDir, "settings.json");
        if (!File.Exists(path)) return new AppSettings();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
    }

    public void Save(string configDir, AppSettings settings)
    {
        Directory.CreateDirectory(configDir);
        var path = Path.Combine(configDir, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
