using SmartIndexManager.App.Services;

namespace SmartIndexManager.App.Tests.Fakes;

public sealed class NullPasswordPrompt : IPasswordPrompt
{
    public Task<string?> RequestAsync(string connectionName, CancellationToken cancellationToken)
        => Task.FromResult<string?>(null);
}
