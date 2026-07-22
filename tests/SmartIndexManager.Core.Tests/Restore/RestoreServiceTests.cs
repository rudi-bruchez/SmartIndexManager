using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Restore;
using Xunit;

namespace SmartIndexManager.Core.Tests.Restore;

public class RestoreServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-restore-").FullName;
    private readonly string _auditDir = Directory.CreateTempSubdirectory("sim-audit-").FullName;
    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Directory.Delete(_auditDir, recursive: true);
    }

    private sealed class FakeProvider : IIndexProvider
    {
        public ServerInfo ServerInfo { get; } = new()
        {
            ServerName = "PROD01", ProductVersion = new Version(16, 0),
            Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 92
        };
        public ProviderCapabilities Capabilities { get; } = new();
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public List<string> ExecutedDdl { get; } = [];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexModel>>([]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct)
        {
            ExecutedDdl.Add(sql);
            return Task.CompletedTask;
        }
        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct) => Task.FromResult(false);
    }

    [Fact]
    public async Task Finds_sessions_and_restores_index()
    {
        var manifest = new Manifest
        {
            ToolVersion = "1.0.0",
            CreatedUtc = DateTime.UtcNow,
            Server = "PROD01",
            Operator = "op",
            Indexes =
            [
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                    File = "Sales.dbo.Orders.IX_A.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                }
            ]
        };
        var serverDir = Path.Combine(_dir, "PROD01");
        var sessionDir = Directory.CreateDirectory(Path.Combine(serverDir, "2026-07-22T10-00-00Z")).FullName;
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_A.sql"), "CREATE NONCLUSTERED INDEX [IX_A] ON [dbo].[Orders] ([CustomerId] ASC);");

        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_dir, "PROD01", CancellationToken.None);
        Assert.Single(sessions);

        var provider = new FakeProvider();
        var result = await service.RestoreAsync(sessions[0], sessions[0].Entries, provider, Path.Combine(_auditDir, "audit.jsonl"), CancellationToken.None);

        Assert.Single(result.Restored);
        Assert.Single(provider.ExecutedDdl);
    }

    [Fact]
    public async Task Multi_entry_restore_marks_all_indexes_restored()
    {
        var manifest = new Manifest
        {
            ToolVersion = "1.0.0",
            CreatedUtc = DateTime.UtcNow,
            Server = "PROD01",
            Operator = "op",
            Indexes =
            [
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                    File = "Sales.dbo.Orders.IX_A.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                },
                new ManifestIndexEntry
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_B",
                    File = "Sales.dbo.Orders.IX_B.sql",
                    Reason = "r", Status = IndexDeletionStatus.Dropped
                }
            ]
        };
        var serverDir = Path.Combine(_dir, "PROD01");
        var sessionDir = Directory.CreateDirectory(Path.Combine(serverDir, "2026-07-22T10-00-00Z")).FullName;
        ManifestStore.Write(Path.Combine(sessionDir, "manifest.json"), manifest);
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_A.sql"), "CREATE NONCLUSTERED INDEX [IX_A] ON [dbo].[Orders] ([CustomerId] ASC);");
        File.WriteAllText(Path.Combine(sessionDir, "Sales.dbo.Orders.IX_B.sql"), "CREATE NONCLUSTERED INDEX [IX_B] ON [dbo].[Orders] ([OrderDate] ASC);");

        var service = new RestoreService();
        var sessions = await service.FindSessionsAsync(_dir, "PROD01", CancellationToken.None);
        var provider = new FakeProvider();
        var result = await service.RestoreAsync(sessions[0], sessions[0].Entries, provider, Path.Combine(_auditDir, "audit.jsonl"), CancellationToken.None);

        Assert.Equal(2, result.Restored.Count);
        Assert.Equal(2, provider.ExecutedDdl.Count);
        var updated = ManifestStore.Read(Path.Combine(sessionDir, "manifest.json"));
        Assert.All(updated.Indexes, e => Assert.Equal(IndexDeletionStatus.Restored, e.Status));
    }
}
