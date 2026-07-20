using System.Runtime.InteropServices;
using SmartIndexManager.App.Localization;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Services;

public sealed class AuthAvailability : IAuthAvailability
{
    private readonly ILocalizer _loc;
    private readonly bool _isWindows;
    private readonly bool _kerberosConfigured;

    public AuthAvailability(ILocalizer loc, bool isWindows, bool kerberosConfigured)
    {
        _loc = loc;
        _isWindows = isWindows;
        _kerberosConfigured = kerberosConfigured;
    }

    // Heuristic for Kerberos on non-Windows: a krb5 config is present. Refined later if needed.
    public static AuthAvailability ForCurrentOs(ILocalizer loc)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        bool kerberos = !isWindows &&
            (File.Exists("/etc/krb5.conf") || Environment.GetEnvironmentVariable("KRB5_CONFIG") is not null);
        return new AuthAvailability(loc, isWindows, kerberos);
    }

    public bool IsAvailable(AuthMode mode) => mode switch
    {
        AuthMode.WindowsIntegrated => _isWindows || _kerberosConfigured,
        _ => true
    };

    public string? UnavailableReason(AuthMode mode)
        => IsAvailable(mode)
            ? null
            : _loc["Auth_WindowsIntegratedUnavailable"];
}
