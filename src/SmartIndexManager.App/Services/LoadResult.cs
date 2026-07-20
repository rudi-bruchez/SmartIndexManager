using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public sealed record LoadResult(
    IIndexProvider Provider,
    ServerInfo Server,
    ProviderCapabilities Capabilities,
    PermissionReport Permissions,
    IReadOnlyList<IndexRowViewModel> Rows);
