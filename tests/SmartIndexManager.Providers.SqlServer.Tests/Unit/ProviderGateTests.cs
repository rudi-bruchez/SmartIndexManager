using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Tests.Integration;

namespace SmartIndexManager.Providers.SqlServer.Tests.Unit;

// Exercises the provider's permission/eligibility gates directly, without Docker: the
// internal constructor is visible to this assembly via InternalsVisibleTo, and a
// RecordingExecutor stands in for a real connection. SqlServerContainerFixture.ScriptRoot()
// is a plain filesystem walk-up (no Docker involved) so index-droppable-check.sql loads for real.
public sealed class ProviderGateTests
{
    private static readonly string ScriptRoot = SqlServerContainerFixture.ScriptRoot();

    private static ServerInfo MakeServerInfo() => new()
    {
        ServerName = "test-server",
        ProductVersion = new Version(16, 0),
        Edition = "Developer Edition",
        Platform = ServerPlatform.OnPremises,
        UptimeDays = 1
    };

    private static ProviderCapabilities MakeCapabilities(bool supportsQueryStore = true) => new()
    {
        SupportsQueryStore = supportsQueryStore,
        SupportsPlanCache = true,
        SupportsColumnstore = true,
        SupportsOnlineDrop = false,
        RequiresDatabaseScopedDmv = false
    };

    private static PermissionReport MakePermissions(bool canViewState = true, bool canAlter = true) => new()
    {
        CanViewState = canViewState,
        CanAlter = canAlter,
        CanAccessQueryStore = true
    };

    private static SqlServerIndexProvider MakeProvider(
        RecordingExecutor executor,
        ProviderCapabilities? capabilities = null,
        PermissionReport? permissions = null)
        => new(executor, ScriptRoot, MakeServerInfo(), capabilities ?? MakeCapabilities(), permissions ?? MakePermissions());

    private static readonly IndexRef SampleIndex = new("Sales", "dbo", "Orders", "IX_X");

    [Fact]
    public async Task GetQueryUsageAsync_WithoutViewStatePermission_ReturnsEmptyAndSkipsExecutor()
    {
        var executor = new RecordingExecutor();
        var provider = MakeProvider(executor, permissions: MakePermissions(canViewState: false));

        var result = await provider.GetQueryUsageAsync(SampleIndex);

        Assert.Empty(result);
        Assert.Equal(0, executor.QueryCount);
    }

    [Fact]
    public async Task GetHintsAsync_WithoutViewStatePermission_ReturnsEmptyAndSkipsExecutor()
    {
        var executor = new RecordingExecutor();
        var provider = MakeProvider(executor, permissions: MakePermissions(canViewState: false));

        var result = await provider.GetHintsAsync(SampleIndex);

        Assert.Empty(result);
        Assert.Equal(0, executor.QueryCount);
    }

    [Fact]
    public async Task DropIndexAsync_WithoutAlterPermission_ThrowsAndNeverExecutes()
    {
        var executor = new RecordingExecutor();
        var provider = MakeProvider(executor, permissions: MakePermissions(canAlter: false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.DropIndexAsync(SampleIndex, TimeSpan.FromSeconds(30)));

        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task DropIndexAsync_WhenDroppableCheckFails_ThrowsAndNeverExecutesDrop()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = false };
        var provider = MakeProvider(executor, permissions: MakePermissions(canAlter: true));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.DropIndexAsync(SampleIndex, TimeSpan.FromSeconds(30)));

        Assert.Equal(0, executor.ExecuteCount);
    }

    [Fact]
    public async Task DropIndexAsync_WhenDroppableCheckSucceeds_ExecutesDrop()
    {
        var executor = new RecordingExecutor { ScalarBoolResult = true };
        var provider = MakeProvider(executor, permissions: MakePermissions(canAlter: true));

        await provider.DropIndexAsync(SampleIndex, TimeSpan.FromSeconds(30));

        Assert.Equal(1, executor.ExecuteCount);
        Assert.Contains("DROP INDEX", executor.LastExecutedSql);
    }

    [Fact]
    public async Task EnableQueryStoreAsync_WhenNotSupported_Throws()
    {
        var executor = new RecordingExecutor();
        var provider = MakeProvider(executor, capabilities: MakeCapabilities(supportsQueryStore: false));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnableQueryStoreAsync("Sales", new QueryStoreSettings()));
    }
}
