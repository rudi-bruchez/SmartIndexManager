using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Execution;

namespace SmartIndexManager.Providers.SqlServer.Mapping;

public static class ServerInfoMapper
{
    public static ServerInfo Map(SqlRow row)
    {
        int engineEdition = row.Get<int>("EngineEdition");
        int? uptime = row.GetRaw("UptimeDays") is null ? null : row.Get<int>("UptimeDays");

        return new ServerInfo
        {
            ServerName = row.Get<string>("ServerName") ?? "",
            ProductVersion = Version.Parse(row.Get<string>("ProductVersion") ?? "0.0"),
            Edition = row.Get<string>("Edition") ?? "",
            Platform = engineEdition switch
            {
                5 => ServerPlatform.AzureSqlDatabase,
                8 => ServerPlatform.AzureManagedInstance,
                _ => ServerPlatform.OnPremises
            },
            UptimeDays = uptime ?? -1
        };
    }
}
