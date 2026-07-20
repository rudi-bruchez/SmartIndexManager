using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class PermissionMapper
{
    public static PermissionReport Map(SqlRow row)
    {
        bool view = row.Get<bool>("CanViewState");
        bool alter = row.Get<bool>("CanAlter");
        bool qs = row.Get<bool>("CanAccessQueryStore");

        var notes = new List<string>();
        if (!view) notes.Add("Missing VIEW SERVER STATE / VIEW DATABASE STATE: usage stats, plan cache and hints are unavailable.");
        if (!alter) notes.Add("Missing ALTER: DROP INDEX and Query Store activation are disabled; script generation stays available.");
        if (!qs) notes.Add("No access to Query Store: Query Store features are unavailable.");

        return new PermissionReport
        {
            CanViewState = view,
            CanAlter = alter,
            CanAccessQueryStore = qs,
            Notes = notes
        };
    }
}
