using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Settings;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly IAppPaths _paths;

    [ObservableProperty] private string _defaultBackupRoot = "";
    [ObservableProperty] private string _snapshotRoot = "";
    [ObservableProperty] private int _snapshotRetentionDays = 90;

    public SettingsViewModel(SettingsService settingsService, IAppPaths paths, ILocalizer loc)
    {
        _settingsService = settingsService;
        _paths = paths;
        var settings = settingsService.Load(paths.ConfigDir);
        DefaultBackupRoot = settings.DefaultBackupRoot ?? _paths.DefaultBackupRoot;
        SnapshotRoot = settings.SnapshotRoot ?? _paths.SnapshotRoot;
        SnapshotRetentionDays = settings.SnapshotRetentionDays;
    }

    [RelayCommand]
    private void Save()
    {
        _settingsService.Save(_paths.ConfigDir, new AppSettings
        {
            DefaultBackupRoot = DefaultBackupRoot,
            SnapshotRoot = SnapshotRoot,
            SnapshotRetentionDays = SnapshotRetentionDays
        });
    }
}
