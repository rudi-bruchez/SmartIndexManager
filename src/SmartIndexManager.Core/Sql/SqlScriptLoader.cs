namespace SmartIndexManager.Core.Sql;

public static class SqlScriptLoader
{
    public static SqlScript Load(string scriptRoot, string name)
    {
        var path = Path.Combine(scriptRoot, $"{name}.sql");
        if (!File.Exists(path))
            throw new FileNotFoundException($"SQL script not found: {path}", path);

        var content = File.ReadAllText(path);
        var header = SqlFileHeaderParser.Parse(content);
        if (!string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
            throw new SqlFileHeaderException(
                $"script '{path}' declares name '{header.Name}' but was loaded as '{name}'");

        return new SqlScript(name, content, header);
    }
}
