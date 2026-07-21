using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Fakes;

// A provider that records the maximum number of GetQueryUsageAsync calls in flight
// at once, mimicking a connection that cannot run concurrent commands.
public sealed class ConcurrencyProbeProvider : IIndexProvider
{
    private int _current;
    public int MaxConcurrent { get; private set; }
    private readonly TaskCompletionSource _gate = new();
    public void Release() => _gate.TrySetResult();

    public required ServerInfo ServerInfo { get; init; }
    public required ProviderCapabilities Capabilities { get; init; }
    public required PermissionReport Permissions { get; init; }
    public IReadOnlyList<IndexModel> Indexes { get; init; } = [];

    public Task<IReadOnlyList<IndexModel>> GetIndexesAsync(IReadOnlyList<string> databases, CancellationToken ct = default)
        => Task.FromResult(Indexes);

    public async Task<IReadOnlyList<QueryUsage>> GetQueryUsageAsync(IndexRef index, CancellationToken ct = default)
    {
        // Deliberately ignores the token while parked: models a command already on the
        // wire that cancellation cannot abort instantly. Overlap here is the real bug.
        var now = Interlocked.Increment(ref _current);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        try { await _gate.Task; }
        finally { Interlocked.Decrement(ref _current); }
        return [];
    }

    public Task<IReadOnlyList<IndexHint>> GetHintsAsync(IndexRef index, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IndexHint>>([]);

    public Task<QueryStoreState> GetQueryStoreStateAsync(string database, CancellationToken ct = default)
        => Task.FromResult(QueryStoreState.Off);

    public Task EnableQueryStoreAsync(string database, QueryStoreSettings settings, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DropIndexAsync(IndexRef index, TimeSpan timeout, CancellationToken ct = default)
        => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
