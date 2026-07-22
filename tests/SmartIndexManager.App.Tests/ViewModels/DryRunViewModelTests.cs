using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.Tests.Fakes;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Core.Safety;
using SmartIndexManager.Core.Scoring;

namespace SmartIndexManager.App.Tests.ViewModels;

public class DryRunViewModelTests
{
    [Fact]
    public async Task LoadAsync_builds_report()
    {
        var vm = await CreateViewModelWithLoadedReportAsync("/docs");

        Assert.NotNull(vm.Report);
        Assert.Single(vm.Report.Entries);
    }

    [Fact]
    public async Task ExportJsonAsync_creates_report_file()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var vm = await CreateViewModelWithLoadedReportAsync(tempRoot);
            var backupDir = Path.Combine(tempRoot, "SmartIndexManager");

            await vm.ExportJsonCommand.ExecuteAsync(null);

            var file = Assert.Single(Directory.GetFiles(backupDir, "dry-run-*.json"));
            Assert.True(new FileInfo(file).Length > 0);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task ExportMarkdownAsync_creates_report_file()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var vm = await CreateViewModelWithLoadedReportAsync(tempRoot);
            var backupDir = Path.Combine(tempRoot, "SmartIndexManager");

            await vm.ExportMarkdownCommand.ExecuteAsync(null);

            var file = Assert.Single(Directory.GetFiles(backupDir, "dry-run-*.md"));
            var content = await File.ReadAllTextAsync(file);
            Assert.Contains("# Dry-run report", content);
        }
        finally
        {
            Directory.Delete(tempRoot, true);
        }
    }

    [Fact]
    public async Task Export_commands_do_nothing_when_report_is_null()
    {
        var basket = new DeletionBasket();
        var vm = new DryRunViewModel(basket, new AppPaths("/cfg", "/docs", "/sql"), new ResxLocalizer());

        await vm.ExportJsonCommand.ExecuteAsync(null);
        await vm.ExportMarkdownCommand.ExecuteAsync(null);
    }

    private static async Task<DryRunViewModel> CreateViewModelWithLoadedReportAsync(string backupRoot)
    {
        var provider = new FakeIndexProvider
        {
            ServerInfo = new ServerInfo { ServerName = "s", ProductVersion = new Version(16, 0), Edition = "Developer", Platform = ServerPlatform.OnPremises, UptimeDays = 100 },
            Capabilities = new ProviderCapabilities { SupportsPlanCache = true },
            Permissions = new PermissionReport { CanViewState = true, CanAlter = true, CanAccessQueryStore = true },
            Indexes = [IndexModelFactory.Nonclustered(name: "IX_A")]
        };
        var basket = new DeletionBasket();
        basket.Add(IndexModelFactory.Nonclustered(name: "IX_A"), new SafetyAssessment(DeletionEligibility.Deletable, null, []), new ConfidenceScore(90, ScoreColor.Green, []));

        var vm = new DryRunViewModel(basket, new AppPaths("/cfg", backupRoot, "/sql"), new ResxLocalizer());
        vm.SetProvider(provider);
        await vm.LoadAsync(CancellationToken.None);
        return vm;
    }
}
