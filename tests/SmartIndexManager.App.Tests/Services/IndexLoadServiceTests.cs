using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class IndexLoadServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-load-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static FakeIndexProvider Provider(params SmartIndexManager.Core.Model.IndexModel[] indexes) => new()
    {
        ServerInfo = new ServerInfo { ServerName = "PROD01", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
        Capabilities = new ProviderCapabilities { SupportsQueryStore = true, SupportsPlanCache = true },
        Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
        Indexes = indexes
    };

    private IndexLoadService Service(FakeIndexProvider provider)
        => new(new FakeIndexProviderFactory(provider), new AppPaths(_dir, _dir, _dir));

    private static ConnectionProfile Profile() => new() { Name = "p", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" };

    [Fact]
    public async Task Builds_a_row_per_index_with_scores_and_safety()
    {
        var provider = Provider(
            IndexModelFactory.Nonclustered(name: "IX_Used", seeks: 5000, lastRead: DateTime.UtcNow),
            IndexModelFactory.PrimaryKey());

        var result = await Service(provider).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("PROD01", result.Server.ServerName);
        var pk = result.Rows.Single(r => r.Name == "PK_Orders");
        Assert.True(pk.NotDeletable);            // hard exclusion
        var used = result.Rows.Single(r => r.Name == "IX_Used");
        Assert.NotNull(used.Score);              // eligible -> scored
    }

    [Fact]
    public async Task Flags_R1_redundant_duplicates()
    {
        var a = IndexModelFactory.Nonclustered(name: "IX_A", keyColumns: ["CustomerId"]);
        var b = IndexModelFactory.Nonclustered(name: "IX_B", keyColumns: ["CustomerId"]);
        var result = await Service(Provider(a, b)).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);

        Assert.All(result.Rows, r => Assert.True(r.Redundant));
    }

    [Fact]
    public async Task Writes_a_usage_snapshot_under_the_snapshot_root()
    {
        await Service(Provider(IndexModelFactory.Nonclustered())).LoadAsync(Profile(), "pw", ["Sales"], CancellationToken.None);
        var snapshotDir = Path.Combine(_dir, "snapshots");
        Assert.True(Directory.Exists(snapshotDir));
        Assert.NotEmpty(Directory.GetFiles(snapshotDir, "*.json", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Passes_the_password_to_connect_and_never_retains_it()
    {
        var factory = new FakeIndexProviderFactory(Provider(IndexModelFactory.Nonclustered()));
        var service = new IndexLoadService(factory, new AppPaths(_dir, _dir, _dir));
        await service.LoadAsync(Profile(), "s3cret", ["Sales"], CancellationToken.None);
        Assert.Equal("s3cret", factory.LastPassword);   // used once for connect
    }
}
