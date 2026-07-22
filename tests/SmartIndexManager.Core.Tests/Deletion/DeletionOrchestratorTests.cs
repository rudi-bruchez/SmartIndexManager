using SmartIndexManager.Core.Audit;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Persistence;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.Deletion;

public class DeletionOrchestratorTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-delete-").FullName;
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
        public ProviderCapabilities Capabilities { get; } = new() { SupportsPlanCache = true };
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public List<IndexRef> Dropped { get; } = [];
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<IndexModel>>([
                new IndexModel
                {
                    Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
                    Type = IndexType.NonclusteredRowstore,
                    KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
                }
            ]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct)
        {
            Dropped.Add(index);
            return Task.CompletedTask;
        }
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct) => Task.FromResult(false);
    }

    private DeletionSession Session() => new("PROD01", "DOMAIN\\rudi", "1.0.0", 92, _dir, DeletionMode.Execute);

    private DeletionBasket Basket()
    {
        var b = new DeletionBasket();
        b.Add(new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
            Type = IndexType.NonclusteredRowstore,
            KeyColumns = [new IndexColumn("CustomerId", SortDirection.Ascending)]
        }, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));
        return b;
    }

    [Fact]
    public async Task Execute_mode_drops_index_writes_backup_manifest_and_audit()
    {
        var provider = new FakeProvider();
        var auditPath = Path.Combine(_auditDir, "audit.jsonl");
        var orchestrator = new DeletionOrchestrator(auditPath);

        var result = await orchestrator.DeleteAsync(provider, Session(), Basket(), new DeletionOptions(TimeSpan.FromSeconds(30)), null, CancellationToken.None);

        Assert.Single(result.Results);
        Assert.Single(provider.Dropped);
        Assert.True(Directory.GetFiles(_dir, "*.sql", SearchOption.AllDirectories).Length >= 1);
        Assert.True(Directory.GetFiles(_dir, "manifest.json", SearchOption.AllDirectories).Length == 1);
        Assert.True(File.Exists(auditPath));
    }
}
