using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Views;

public partial class IndexDetailView : UserControl
{
    public IndexDetailView() => AvaloniaXamlLoader.Load(this);

    private async void OnCopyDdl(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (DataContext is IndexDetailViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                var data = new DataTransfer();
                data.Add(DataTransferItem.CreateText(vm.Ddl));
                await clipboard.SetDataAsync(data);
            }
        }
        catch
        {
            // Clipboard may be unavailable; swallow to avoid an unhandled exception in an async-void handler.
        }
    }
}
