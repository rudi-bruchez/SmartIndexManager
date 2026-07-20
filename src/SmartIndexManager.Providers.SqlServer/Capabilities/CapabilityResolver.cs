using SmartIndexManager.Core.Model;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Capabilities;

public static class CapabilityResolver
{
    public static ProviderCapabilities Resolve(ServerInfo info)
    {
        bool azure = info.Platform is ServerPlatform.AzureSqlDatabase or ServerPlatform.AzureManagedInstance;
        bool queryStore = azure || info.ProductVersion.Major >= 13;          // 2016+
        bool columnstore = azure || info.ProductVersion.Major >= 11;         // 2012 nonclustered CS
        bool enterprise = info.Edition.Contains("Enterprise", StringComparison.OrdinalIgnoreCase)
                       || info.Edition.Contains("Developer", StringComparison.OrdinalIgnoreCase);

        return new ProviderCapabilities
        {
            SupportsQueryStore = queryStore,
            SupportsPlanCache = true,
            SupportsColumnstore = columnstore,
            // Informational only. The MVP DROP is deliberately a plain DROP INDEX with no
            // implicit WITH (ONLINE = ON) (design spec section 6). This flag lets the App
            // offer an online option later; DropIndexAsync does not read it in the MVP.
            SupportsOnlineDrop = azure || enterprise,
            RequiresDatabaseScopedDmv = info.Platform == ServerPlatform.AzureSqlDatabase
        };
    }
}
