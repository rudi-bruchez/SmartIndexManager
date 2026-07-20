namespace SmartIndexManager.Core.Model;

public sealed record IndexSizeInfo(long Pages, long Rows, double SizeMb)
{
    public static IndexSizeInfo Empty { get; } = new(0, 0, 0);
}
