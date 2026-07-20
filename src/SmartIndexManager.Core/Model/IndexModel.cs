namespace SmartIndexManager.Core.Model;

public sealed record IndexModel
{
    public required string Database { get; init; }
    public required string Schema { get; init; }
    public required string Table { get; init; }
    public required string Name { get; init; }
    public required IndexType Type { get; init; }
    public IReadOnlyList<IndexColumn> KeyColumns { get; init; } = [];
    public IReadOnlyList<string> IncludedColumns { get; init; } = [];
    public string? FilterPredicate { get; init; }
    public bool IsUnique { get; init; }
    public ConstraintKind Constraint { get; init; } = ConstraintKind.None;
    public bool IsDisabled { get; init; }
    public bool IsOnView { get; init; }
    public bool IsOnSystemTable { get; init; }
    public bool IsPartitioned { get; init; }
    // Filegroup or partition-scheme name the index lives on. Emitted as ON [DataSpace]
    // so a restore recreates the index on its original filegroup, not the default.
    // Null means the default filegroup (no ON clause emitted).
    public string? DataSpace { get; init; }
    public IndexUsageStats Usage { get; init; } = IndexUsageStats.Empty;
    public IndexSizeInfo Size { get; init; } = IndexSizeInfo.Empty;
    public IndexOptions Options { get; init; } = new();
    // MVP simplification: provider-specific properties are display-only strings.
    // The Core never reasons on them (only on the common properties above), so a
    // string map is enough for the MVP. If a later version needs typed values, widen
    // to a small union type then; do not pre-generalize now (YAGNI).
    public IReadOnlyDictionary<string, string> ProviderProperties { get; init; }
        = new Dictionary<string, string>();
}
