namespace SmartIndexManager.Core.Provider;

using SmartIndexManager.Core.Model;

public sealed record IndexRef(string Database, string Schema, string Table, string Index)
{
    public static IndexRef Of(IndexModel index)
        => new(index.Database, index.Schema, index.Table, index.Name);
}
