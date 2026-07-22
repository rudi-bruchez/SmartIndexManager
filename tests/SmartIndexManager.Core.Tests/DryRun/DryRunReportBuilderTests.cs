using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;
using Xunit;

namespace SmartIndexManager.Core.Tests.DryRun;

public class DryRunReportBuilderTests
{
    private sealed class FakeProvider : IIndexProvider
    {
        public ServerInfo ServerInfo { get; } = new()
        {
            ServerName = "PROD01", ProductVersion = new Version(16, 0),
            Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 92
        };
        public ProviderCapabilities Capabilities { get; } = new()
            { SupportsPlanCache = true, SupportsQueryStore = true };
        public PermissionReport Permissions { get; } = new()
            { CanViewState = true, CanAlter = true, CanAccessQueryStore = true };

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexModel>>([]);
        public Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<QueryUsage>>([new QueryUsage("SELECT 1", 5, DateTime.UtcNow, UsageSource.PlanCache)]);
        public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct) => Task.FromResult<IReadOnlyList<IndexHint>>([]);
        public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct) => Task.FromResult(QueryStoreState.Off);
        public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct) => Task.CompletedTask;
        public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct) => Task.CompletedTask;
        public Task ExecuteDdlAsync(string database, string sql, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IndexExistsAsync(string database, string schema, string table, string index, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task Builds_report_with_usage_and_hints()
    {
        var basket = new DeletionBasket();
        var index = new IndexModel
        {
            Database = "Sales", Schema = "dbo", Table = "Orders", Name = "IX_A",
            Type = IndexType.NonclusteredRowstore,
            Usage = new IndexUsageStats(0, 0, 0, 100, null, null),
            Size = new IndexSizeInfo(100, 1000, 8.0)
        };
        basket.Add(index, new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));

        var report = await new DryRunReportBuilder().BuildAsync(new FakeProvider(), basket, CancellationToken.None);

        Assert.Equal("PROD01", report.Server);
        Assert.Single(report.Entries);
        Assert.Single(report.Entries[0].Queries);
        Assert.Equal(8.0, report.TotalSizeMb);
    }
}
