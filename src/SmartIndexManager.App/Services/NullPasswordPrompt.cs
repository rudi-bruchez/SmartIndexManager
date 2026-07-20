namespace SmartIndexManager.App.Services;

public sealed class NullPasswordPrompt : IPasswordPrompt
{
    public Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
