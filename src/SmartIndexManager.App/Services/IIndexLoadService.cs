namespace SmartIndexManager.App.Services;

public interface IIndexLoadService
{
    /// <summary>Loads indexes from the selected profile.</summary>
    /// <returns>A result whose <see cref="LoadResult.Provider"/> is owned by the caller and must be disposed.</returns>
    Task<LoadResult> LoadAsync(
        ConnectionProfile profile, string? password,
        IReadOnlyList<string> databases, CancellationToken cancellationToken);
}
