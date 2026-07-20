using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class PermissionStatusViewModel : ViewModelBase
{
    private readonly ILocalizer _loc;

    [ObservableProperty] private bool _usageAvailable = true;
    [ObservableProperty] private bool _readOnly;

    public IReadOnlyList<string> Messages { get; private set; } = [];

    public PermissionStatusViewModel(ILocalizer loc) => _loc = loc;

    public void Update(PermissionReport permissions, ProviderCapabilities capabilities)
    {
        var messages = new List<string>();
        UsageAvailable = permissions.CanViewState;
        if (!permissions.CanViewState) messages.Add(_loc["Permission_UsageUnavailable"]);

        ReadOnly = !permissions.CanAlter;
        if (!permissions.CanAlter) messages.Add(_loc["Permission_ReadOnly"]);

        Messages = messages;
        OnPropertyChanged(nameof(Messages));
    }
}
