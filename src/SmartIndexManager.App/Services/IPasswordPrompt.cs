namespace SmartIndexManager.App.Services;

public interface IPasswordPrompt
{
    // Returns null if the user cancels. The result is used once for connect and never stored.
    Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken);
}
