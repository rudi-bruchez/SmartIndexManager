using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace SmartIndexManager.Providers.SqlServer.Tests.Integration;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    private MsSqlContainer? _container;
    public string ConnectionString { get; private set; } = "";

    public string Database => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    public static string ScriptRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        return Path.Combine(dir!, "sql", "sqlserver");
    }

    public async Task InitializeAsync()
    {
        if (!DockerAvailable.Value) return;
        _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU14-ubuntu-22.04").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
        await SeedAsync();
    }

    private async Task SeedAsync()
    {
        await using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE dbo.Orders (Id int IDENTITY PRIMARY KEY, CustomerId int NOT NULL, OrderDate date NULL, Total money NULL);
            CREATE NONCLUSTERED INDEX IX_Orders_Customer ON dbo.Orders (CustomerId);
            CREATE NONCLUSTERED INDEX IX_Orders_Unused ON dbo.Orders (OrderDate) INCLUDE (Total);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerContainerFixture>;
