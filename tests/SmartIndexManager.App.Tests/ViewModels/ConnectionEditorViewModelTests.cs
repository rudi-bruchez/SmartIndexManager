using SmartIndexManager.App.Services;
using SmartIndexManager.App.ViewModels;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.ViewModels;

public class ConnectionEditorViewModelTests
{
    [Fact]
    public void ToProfile_captures_the_edited_fields()
    {
        var vm = new ConnectionEditorViewModel(new AuthAvailability(isWindows: true, kerberosConfigured: false))
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
        Assert.True(new ConnectionEditorViewModel(new AuthAvailability(true, false)).WindowsIntegratedAvailable);
        Assert.False(new ConnectionEditorViewModel(new AuthAvailability(false, false)).WindowsIntegratedAvailable);
    }
}
