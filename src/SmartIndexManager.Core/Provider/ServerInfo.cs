namespace SmartIndexManager.Core.Provider;

public sealed record ServerInfo
{
    public required string ServerName { get; init; }
    public required Version ProductVersion { get; init; }
    public required string Edition { get; init; }
    public required ServerPlatform Platform { get; init; }
    // Days since the engine started. -1 means unknown (for example Azure SQL Database,
    // where the server-scoped uptime DMV is not readable); consumers treat -1 as
    // "reliability unknown" and lean on the Azure platform badge instead.
    public required int UptimeDays { get; init; }
}
