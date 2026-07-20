using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

public sealed class FakeIndexProviderFactory : IIndexProviderFactory
{
    private readonly IIndexProvider _provider;
    public ConnectionRequest? LastRequest { get; private set; }
    public string? LastPassword { get; private set; }

    public FakeIndexProviderFactory(IIndexProvider provider) => _provider = provider;

    public Task<IIndexProvider> ConnectAsync(ConnectionRequest request, string? password, CancellationToken ct = default)
    {
        LastRequest = request;
        LastPassword = password;
        return Task.FromResult(_provider);
    }
}
