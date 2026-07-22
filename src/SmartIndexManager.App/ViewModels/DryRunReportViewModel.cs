using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.DryRun;

namespace SmartIndexManager.App.ViewModels;

public sealed class DryRunReportViewModel : ViewModelBase
{
    public DryRunReport Report { get; }
    public IReadOnlyList<DryRunReportEntryViewModel> Entries { get; }
    public string ReliabilityText { get; }
    public string SummaryText { get; }

    public DryRunReportViewModel(DryRunReport report, ILocalizer loc)
    {
        Report = report;
        Entries = report.Entries.Select(e => new DryRunReportEntryViewModel(e)).ToList();
        ReliabilityText = string.Format(loc["DryRun_Reliability"], report.ReliabilityBadge);
        SummaryText = string.Format(loc["DryRun_Summary"], report.Entries.Count, report.TotalSizeMb);
    }
}

public sealed class DryRunReportEntryViewModel
{
    private readonly DryRunReportEntry _entry;

    public string DisplayName => $"{_entry.Database}.{_entry.Schema}.{_entry.Table}.{_entry.Index}";
    public int Score => _entry.Score;
    public string WarningsText => string.Join("; ", _entry.Warnings.Select(w => w.Message));

    public DryRunReportEntryViewModel(DryRunReportEntry entry) => _entry = entry;
}
