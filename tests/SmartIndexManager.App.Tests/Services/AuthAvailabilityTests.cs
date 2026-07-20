using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class AuthAvailabilityTests
{
    [Fact]
    public void Windows_integrated_is_available_on_windows()
        => Assert.True(new AuthAvailability(isWindows: true, kerberosConfigured: false).IsAvailable(AuthMode.WindowsIntegrated));

    [Fact]
    public void Windows_integrated_is_unavailable_on_non_windows_without_kerberos()
    {
        var a = new AuthAvailability(isWindows: false, kerberosConfigured: false);
        Assert.False(a.IsAvailable(AuthMode.WindowsIntegrated));
        Assert.NotNull(a.UnavailableReason(AuthMode.WindowsIntegrated));
    }

    [Fact]
    public void Windows_integrated_becomes_available_with_kerberos()
        => Assert.True(new AuthAvailability(isWindows: false, kerberosConfigured: true).IsAvailable(AuthMode.WindowsIntegrated));

    [Theory]
    [InlineData(AuthMode.SqlLogin)]
    [InlineData(AuthMode.EntraIdInteractive)]
    public void Sql_and_entra_are_always_available(AuthMode mode)
        => Assert.True(new AuthAvailability(isWindows: false, kerberosConfigured: false).IsAvailable(mode));
}
