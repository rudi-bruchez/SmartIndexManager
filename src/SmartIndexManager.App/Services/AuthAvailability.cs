using System.Runtime.InteropServices;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public sealed class AuthAvailability : IAuthAvailability
{
    private readonly bool _isWindows;
    private readonly bool _kerberosConfigured;

    public AuthAvailability(bool isWindows, bool kerberosConfigured)
    {
        _isWindows = isWindows;
        _kerberosConfigured = kerberosConfigured;
    }

    // Heuristic for Kerberos on non-Windows: a krb5 config is present. Refined later if needed.
    public static AuthAvailability ForCurrentOs()
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool kerberos = !isWindows &&
            (File.Exists("/etc/krb5.conf") || Environment.GetEnvironmentVariable("KRB5_CONFIG") is not null);
        return new AuthAvailability(isWindows, kerberos);
    }

    public bool IsAvailable(AuthMode mode) => mode switch
    {
        AuthMode.WindowsIntegrated => _isWindows || _kerberosConfigured,
        _ => true
    };

    public string? UnavailableReason(AuthMode mode)
        => IsAvailable(mode)
            ? null
            : "Windows Integrated authentication needs Windows or a Kerberos-configured host. Use SQL or Entra ID instead.";
}
