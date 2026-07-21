using Material.Icons;

namespace SmartIndexManager.App.ViewModels;

public sealed record NavigationDestination(
    string Title, MaterialIconKind IconKind, object PageViewModel, bool IsEnabled = true);
