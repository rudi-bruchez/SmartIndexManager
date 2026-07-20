namespace SmartIndexManager.Core.Model;

public enum IndexType
{
    Heap,
    ClusteredRowstore,
    NonclusteredRowstore,
    ClusteredColumnstore,
    NonclusteredColumnstore,
    Xml,
    Spatial,
    FullText,
    Hypothetical
}
