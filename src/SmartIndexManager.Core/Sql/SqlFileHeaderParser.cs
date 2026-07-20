namespace SmartIndexManager.Core.Sql;

public static class SqlFileHeaderParser
{
    private const string Prefix = "-- sim:";

    public static SqlFileHeader Parse(string fileContent)
    {
        var pairs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool seenMetadata = false;
        foreach (var raw in fileContent.Split('\n'))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            {
                if (seenMetadata) break;
                continue;
            }

            seenMetadata = true;
            var body = line[Prefix.Length..].Trim();
            int eq = body.IndexOf('=');
            if (eq <= 0) throw new SqlFileHeaderException($"malformed metadata line: '{line}'");
            pairs[body[..eq].Trim()] = body[(eq + 1)..].Trim();
        }

        string name = Require(pairs, "name");
        string columnsRaw = Require(pairs, "columns");
        var columns = columnsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (columns.Length == 0) throw new SqlFileHeaderException("columns must list at least one column");

        Version minVersion = new(0, 0);
        if (pairs.TryGetValue("minversion", out var mv))
        {
            if (!Version.TryParse(mv, out var parsed))
                throw new SqlFileHeaderException($"invalid minversion: '{mv}'");
            minVersion = parsed;
        }

        var azure = AzureSupport.Supported;
        if (pairs.TryGetValue("azure", out var az))
            azure = az.ToLowerInvariant() switch
            {
                "supported" => AzureSupport.Supported,
                "unsupported" => AzureSupport.Unsupported,
                "only" => AzureSupport.Only,
                _ => throw new SqlFileHeaderException($"invalid azure value: '{az}'")
            };

        return new SqlFileHeader(name, minVersion, azure, columns);
    }

    private static string Require(Dictionary<string, string> pairs, string key)
        => pairs.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v
            : throw new SqlFileHeaderException($"missing required metadata: {key}");
}
