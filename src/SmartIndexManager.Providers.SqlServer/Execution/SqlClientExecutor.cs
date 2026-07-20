using System.Data;
using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Sql;

namespace SmartIndexManager.Providers.SqlServer.Execution;

public sealed class SqlClientExecutor : ISqlExecutor
{
    private readonly SqlConnection _connection;

    public SqlClientExecutor(SqlConnection connection) => _connection = connection;

    public async Task<IReadOnlyList<SqlRow>> QueryAsync(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(script.Sql, parameters, timeout: null);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        ValidateColumns(script, reader);

        var rows = new List<SqlRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // DBNull is normalized to null here so mappers never see DBNull (SqlRow also guards).
            var cells = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                cells[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(new SqlRow(cells));
        }
        return rows;
    }

    public async Task<T?> ScalarAsync<T>(
        SqlScript script, IReadOnlyDictionary<string, object?>? parameters, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(script.Sql, parameters, timeout: null);
        var value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is null or DBNull) return default;
        var target = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(value, target);
    }

    public async Task<int> ExecuteAsync(
        string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(sql, parameters, timeout);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangeDatabaseAsync(string database, CancellationToken cancellationToken)
    {
        // Validate against the catalog, then use the driver's own ChangeDatabase (no SQL injection surface).
        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM sys.databases WHERE name = @db;";
        cmd.Parameters.Add(new SqlParameter("@db", database));
        var exists = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (exists is null) throw new InvalidOperationException($"unknown database: {database}");
        await _connection.ChangeDatabaseAsync(database, cancellationToken).ConfigureAwait(false);
    }

    private SqlCommand CreateCommand(string sql, IReadOnlyDictionary<string, object?>? parameters, TimeSpan? timeout)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        if (timeout is TimeSpan t) cmd.CommandTimeout = (int)t.TotalSeconds;
        if (parameters is not null)
            foreach (var (key, value) in parameters)
                cmd.Parameters.Add(new SqlParameter(key, value ?? DBNull.Value));
        return cmd;
    }

    private static void ValidateColumns(SqlScript script, SqlDataReader reader)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++) present.Add(reader.GetName(i));

        var missing = script.ExpectedColumns.Where(c => !present.Contains(c)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"script '{script.Name}' is missing declared column(s): {string.Join(", ", missing)}");
    }

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}
