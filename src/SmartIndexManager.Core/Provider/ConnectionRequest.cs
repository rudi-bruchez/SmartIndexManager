namespace SmartIndexManager.Core.Provider;

public sealed record ConnectionRequest
{
    public required string Server { get; init; }
    public int? Port { get; init; }
    public required AuthMode Auth { get; init; }
    public string? Login { get; init; }                 // required for SqlLogin, ignored otherwise
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public string ApplicationName { get; init; } = "SmartIndexManager";
    public int ConnectTimeoutSeconds { get; init; } = 15;
}
