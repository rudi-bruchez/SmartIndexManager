using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace SmartIndexManager.App.Views;

public partial class ConnectionManagerDialog : Window
{
    public ConnectionManagerDialog() => AvaloniaXamlLoader.Load(this);

    private void OnClose(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
