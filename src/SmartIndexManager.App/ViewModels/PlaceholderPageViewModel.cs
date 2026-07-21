using Material.Icons;

namespace SmartIndexManager.App.ViewModels;

public sealed class PlaceholderPageViewModel : ViewModelBase
{
    public string Title { get; }
    public MaterialIconKind IconKind { get; }
    public string Message { get; }

    public PlaceholderPageViewModel(string title, MaterialIconKind iconKind, string message)
    {
        Title = title;
        IconKind = iconKind;
        Message = message;
    }
}
