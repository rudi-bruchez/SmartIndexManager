namespace SmartIndexManager.Providers.SqlServer.Execution;

public sealed class SqlRow
{
    private readonly IReadOnlyDictionary<string, object?> _cells;

    public SqlRow(IReadOnlyDictionary<string, object?> cells) => _cells = cells;

    public bool Has(string column) => _cells.ContainsKey(column);

    public object? GetRaw(string column)
        => _cells.TryGetValue(column, out var v) && v is not DBNull ? v : null;

    public T? Get<T>(string column)
    {
        var raw = GetRaw(column);
        if (raw is null) return default;
        if (raw is T typed) return typed;

        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        // SQL Server bit surfaces as bool; ints from CASE expressions must coerce to bool too.
        if (target == typeof(bool)) return (T)(object)Convert.ToBoolean(raw);
        return (T)Convert.ChangeType(raw, target);
    }
}
