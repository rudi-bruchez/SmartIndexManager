using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

/// <param name="Provider">The live provider connection. The caller owns disposal.</param>
public sealed record LoadResult(
    IIndexProvider Provider,
    ServerInfo Server,
    ProviderCapabilities Capabilities,
    PermissionReport Permissions,
    IReadOnlyList<IndexRowViewModel> Rows);
