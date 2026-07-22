using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmartIndexManager.App.Services;

public sealed partial class PasswordPromptViewModel : ObservableObject
{
    public string ConnectionName { get; }
    public TaskCompletionSource<string?> Result { get; } = new();

    [ObservableProperty] private string _password = "";

    public PasswordPromptViewModel(string connectionName) => ConnectionName = connectionName;

    [RelayCommand]
    private void Connect() => Result.TrySetResult(Password);

    [RelayCommand]
    private void Cancel() => Result.TrySetResult(null);
}
