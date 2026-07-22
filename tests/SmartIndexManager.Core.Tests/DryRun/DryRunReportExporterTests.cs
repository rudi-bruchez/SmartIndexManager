using SmartIndexManager.Core.DryRun;
using Xunit;

namespace SmartIndexManager.Core.Tests.DryRun;

public class DryRunReportExporterTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-dryrun-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static DryRunReport Sample() => new()
    {
        Server = "PROD01",
        Databases = ["Sales"],
        CreatedUtc = new DateTime(2026, 07, 22, 10, 0, 0, DateTimeKind.Utc),
        UptimeDays = 92,
        ReliabilityBadge = DryRunReliabilityBadge.Green,
        Entries =
        [
            new DryRunReportEntry
            {
                Database = "Sales", Schema = "dbo", Table = "Orders", Index = "IX_A",
                Type = "NonclusteredRowstore", Key = "CustomerId", Includes = "",
                SizeMb = 12.5, Seeks = 0, Scans = 0, Updates = 1_200_000,
                Score = 94
            }
        ]
    };

    [Fact]
    public void ExportJson_writes_report()
    {
        var path = Path.Combine(_dir, "report.json");
        DryRunReportExporter.ExportJson(path, Sample());
        var json = File.ReadAllText(path);
        Assert.Contains("\"server\": \"PROD01\"", json);
        Assert.Contains("IX_A", json);
    }

    [Fact]
    public void ExportMarkdown_writes_report()
    {
        var path = Path.Combine(_dir, "report.md");
        DryRunReportExporter.ExportMarkdown(path, Sample());
        var md = File.ReadAllText(path);
        Assert.Contains("PROD01", md);
        Assert.Contains("IX_A", md);
    }
}
