using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public interface IIndexLoadService
{
    IIndexProvider? CurrentProvider { get; }

    Task<LoadResult> LoadAsync(
        ConnectionProfile profile, string? password,
        IReadOnlyList<string> databases, CancellationToken cancellationToken);
}
