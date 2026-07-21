using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public partial class IndexDetailView : UserControl
{
    public IndexDetailView() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyDdl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is IndexDetailViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.Ddl);
    }
}
