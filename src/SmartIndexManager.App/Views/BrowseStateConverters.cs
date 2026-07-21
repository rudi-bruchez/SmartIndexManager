using Avalonia.Data.Converters;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public static class BrowseStateConverters
{
    public static readonly IValueConverter IsGridVisible = new FuncValueConverter<BrowseState, bool>(
        s => s is BrowseState.Ready or BrowseState.Empty);
    public static readonly IValueConverter IsDisconnected = new FuncValueConverter<BrowseState, bool>(
        s => s == BrowseState.Disconnected);
    public static readonly IValueConverter IsError = new FuncValueConverter<BrowseState, bool>(
        s => s == BrowseState.Error);
}
