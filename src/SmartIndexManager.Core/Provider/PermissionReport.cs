namespace SmartIndexManager.Core.Provider;

public sealed record PermissionReport
{
    public required bool CanViewState { get; init; }        // VIEW SERVER STATE or VIEW DATABASE STATE
    public required bool CanAlter { get; init; }            // ALTER rights for DROP and Query Store
    public required bool CanAccessQueryStore { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}
