namespace SmartIndexManager.Core.Provider;

public sealed record IndexHint(string Reference, string Kind);   // Kind: "query hint" or "plan guide"
