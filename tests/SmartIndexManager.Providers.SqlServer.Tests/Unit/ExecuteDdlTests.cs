using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;
using Xunit;

namespace SmartIndexManager.Providers.SqlServer.Tests.Unit;

internal static class TestScriptRoot
{
    public static string Path()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(System.IO.Path.Combine(dir, "sql", "sqlserver")))
            dir = Directory.GetParent(dir)?.FullName;
        return System.IO.Path.Combine(dir!, "sql", "sqlserver");
    }
}

public class ExecuteDdlTests
{
    private static SqlServerIndexProvider Provider(ISqlExecutor executor)
        => new(executor, TestScriptRoot.Path(),
            new ServerInfo { ServerName = "x", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            new ProviderCapabilities(),
            new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true });

    [Fact]
    public async Task Validate_database_then_execute_ddl()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = true };
        var provider = Provider(executor);

        await provider.ExecuteDdlAsync("Sales", "CREATE INDEX IX_Tmp ON dbo.T(A);", CancellationToken.None);

        Assert.Equal(1, executor.ScalarCount);
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Contains("CREATE INDEX", executor.LastExecutedSql);
    }

    [Fact]
    public async Task Unknown_database_throws_before_executing_ddl()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = false };
        var provider = Provider(executor);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ExecuteDdlAsync("MissingDb", "CREATE INDEX IX_Tmp ON dbo.T(A);", CancellationToken.None));

        Assert.Equal(1, executor.ScalarCount);
        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task IndexExistsAsync_returns_scalar_result()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = true };
        var provider = Provider(executor);

        var exists = await provider.IndexExistsAsync("Sales", "dbo", "Orders", "IX_A", CancellationToken.None);

        Assert.True(exists);
        Assert.Equal(2, executor.ScalarCount);
        Assert.Equal(1, executor.ChangeDatabaseCount);
    }
}
