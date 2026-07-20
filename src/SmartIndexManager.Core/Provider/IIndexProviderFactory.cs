namespace SmartIndexManager.Core.Provider;

public interface IIndexProviderFactory
{
    // password is used once to build the connection string and never stored.
    // Null for WindowsIntegrated and EntraIdInteractive.
    Task<IIndexProvider> ConnectAsync(
        ConnectionRequest request, string? password, CancellationToken cancellationToken = default);
}
