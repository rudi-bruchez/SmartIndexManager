using CommunityToolkit.Mvvm.ComponentModel;
using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.ViewModels;

public sealed partial class ConnectionEditorViewModel : ViewModelBase
{
    private readonly IAuthAvailability _authAvailability;

    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _server = "";
    [ObservableProperty] private int? _port;
    [ObservableProperty] private string? _login;
    [ObservableProperty] private AuthMode _auth = AuthMode.SqlLogin;
    [ObservableProperty] private bool _encrypt = true;
    [ObservableProperty] private bool _trustServerCertificate;

    public ConnectionEditorViewModel(IAuthAvailability auth) => _authAvailability = auth;

    public IReadOnlyList<AuthMode> AuthModes { get; } =
        [AuthMode.WindowsIntegrated, AuthMode.SqlLogin, AuthMode.EntraIdInteractive];

    public bool WindowsIntegratedAvailable => _authAvailability.IsAvailable(AuthMode.WindowsIntegrated);
    public string? WindowsIntegratedReason => _authAvailability.UnavailableReason(AuthMode.WindowsIntegrated);

    public ConnectionProfile ToProfile() => new()
    {
        Name = Name, Server = Server, Port = Port, Login = Login,
        Auth = Auth, Encrypt = Encrypt, TrustServerCertificate = TrustServerCertificate
    };

    public static ConnectionEditorViewModel FromProfile(ConnectionProfile p, IAuthAvailability auth) => new(auth)
    {
        Name = p.Name, Server = p.Server, Port = p.Port, Login = p.Login,
        Auth = p.Auth, Encrypt = p.Encrypt, TrustServerCertificate = p.TrustServerCertificate
    };
}
