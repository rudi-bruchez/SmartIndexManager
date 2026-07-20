namespace SmartIndexManager.Core.Model;

public sealed record ProviderCapabilities
{
    public bool SupportsQueryStore { get; init; }
    public bool SupportsPlanCache { get; init; }
    public bool SupportsColumnstore { get; init; }
    public bool SupportsOnlineDrop { get; init; }
    public bool RequiresDatabaseScopedDmv { get; init; }
}
