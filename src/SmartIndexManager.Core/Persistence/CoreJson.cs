using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartIndexManager.Core.Persistence;

public static class CoreJson
{
    // Enum values are camelCased to match the manifest example in the design spec
    // (section 9 shows "status": "dropped", "mode": "execute"). PropertyNamingPolicy
    // alone does NOT affect enum string values; the converter needs its own policy.
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };
}
