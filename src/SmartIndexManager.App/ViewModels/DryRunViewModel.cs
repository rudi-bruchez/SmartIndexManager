using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Deletion;
using SmartIndexManager.Core.DryRun;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class DryRunViewModel : ViewModelBase
{
    private IIndexProvider? _provider;
    private readonly DeletionBasket _basket;
    private readonly IAppPaths _paths;
    private readonly ILocalizer _loc;

    [ObservableProperty] private DryRunReportViewModel? _report;

    public DryRunViewModel(DeletionBasket basket, IAppPaths paths, ILocalizer loc)
    {
        _basket = basket;
        _paths = paths;
        _loc = loc;
    }

    public void SetProvider(IIndexProvider provider) => _provider = provider;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (_provider is null) throw new InvalidOperationException("Provider not set.");
        var report = await new DryRunReportBuilder().BuildAsync(_provider, _basket, cancellationToken).ConfigureAwait(true);
        Report = new DryRunReportViewModel(report, _loc);
    }

    [RelayCommand]
    private async Task ExportJsonAsync()
    {
        if (Report is null) return;
        var path = Path.Combine(_paths.DefaultBackupRoot, $"dry-run-{DateTime.UtcNow:yyyyMMddTHHmmss}.json");
        await Task.Run(() => DryRunReportExporter.ExportJson(path, Report.Report));
    }

    [RelayCommand]
    private async Task ExportMarkdownAsync()
    {
        if (Report is null) return;
        var path = Path.Combine(_paths.DefaultBackupRoot, $"dry-run-{DateTime.UtcNow:yyyyMMddTHHmmss}.md");
        await Task.Run(() => DryRunReportExporter.ExportMarkdown(path, Report.Report));
    }
}
