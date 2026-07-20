namespace SmartIndexManager.Core.Model;

public sealed record IndexOptions
{
    public int? FillFactor { get; init; }
    public bool PadIndex { get; init; }
    public bool AllowRowLocks { get; init; } = true;
    public bool AllowPageLocks { get; init; } = true;
    public bool IgnoreDupKey { get; init; }
    public DataCompression Compression { get; init; } = DataCompression.None;
}
