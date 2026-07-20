using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

// A persisted named connection. NEVER contains a password (spec security constraint).
public sealed record ConnectionProfile
{
    public required string Name { get; init; }
    public required string Server { get; init; }
    public int? Port { get; init; }
    public bool Encrypt { get; init; } = true;
    public bool TrustServerCertificate { get; init; }
    public required AuthMode Auth { get; init; }
    public string? Login { get; init; }   // used for SqlLogin, optional for Entra
}
