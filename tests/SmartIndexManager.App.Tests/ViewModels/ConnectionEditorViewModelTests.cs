using SmartIndexManager.App.Localization;
using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionEditorViewModelTests
{
    private static IAuthAvailability Avail(bool isWindows, bool kerberosConfigured)
        => new AuthAvailability(new ResxLocalizer(), isWindows, kerberosConfigured);

    [Fact]
    public void ToProfile_captures_the_edited_fields()
    {
        var vm = new ConnectionEditorViewModel(Avail(isWindows: true, kerberosConfigured: false))
        {
            Name = "prod", Server = "PROD01", Port = 14330, Auth = AuthMode.SqlLogin, Login = "app"
        };
        var profile = vm.ToProfile();
        Assert.Equal("PROD01", profile.Server);
        Assert.Equal(14330, profile.Port);
        Assert.Equal(AuthMode.SqlLogin, profile.Auth);
    }

    [Fact]
    public void Windows_integrated_availability_reflects_the_platform()
    {
        Assert.True(new ConnectionEditorViewModel(Avail(true, false)).WindowsIntegratedAvailable);
        Assert.False(new ConnectionEditorViewModel(Avail(false, false)).WindowsIntegratedAvailable);
    }
}
