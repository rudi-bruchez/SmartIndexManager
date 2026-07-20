using SmartIndexManager.App.Services;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.App.Tests.Services;

public class ConnectionStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("sim-conn-").FullName;
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConnectionStore Store()
        => new(new AppPaths(configDir: _dir, documentsDir: _dir, sqlScriptRoot: _dir));

    [Fact]
    public void Save_then_Load_roundtrips_profiles()
    {
        var profiles = new[]
        {
            new ConnectionProfile { Name = "prod", Server = "PROD01", Auth = AuthMode.SqlLogin, Login = "app" },
            new ConnectionProfile { Name = "local", Server = ".", Port = 14330, Auth = AuthMode.WindowsIntegrated }
        };

        Store().Save(profiles);
        var loaded = Store().Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("PROD01", loaded[0].Server);
        Assert.Equal(AuthMode.WindowsIntegrated, loaded[1].Auth);
    }

    [Fact]
    public void Load_returns_empty_when_no_file_exists()
        => Assert.Empty(Store().Load());

    [Fact]
    public void Persisted_json_never_contains_a_password_property()
    {
        Store().Save(new[] { new ConnectionProfile { Name = "p", Server = "s", Auth = AuthMode.SqlLogin, Login = "u" } });
        var json = File.ReadAllText(Path.Combine(_dir, "connections.json"));
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
    }
}
