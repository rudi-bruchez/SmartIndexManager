using SmartIndexManager.App.ViewModels;

namespace SmartIndexManager.App.Services;

public interface IDialogService
{
    Task ShowConnectionManagerAsync(ConnectionManagerViewModel vm);
}
