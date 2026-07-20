using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;
using SmartIndexManager.Providers.SqlServer.Connection;

namespace SmartIndexManager.Providers.SqlServer.Tests.Connection;

public class SqlServerConnectionFactoryTests
{
    private static ConnectionRequest Base(AuthMode auth) => new()
    {
        Server = "srv01", Auth = auth, Login = "app", Encrypt = true, TrustServerCertificate = true
    };

    private static SqlConnectionStringBuilder Parse(string cs) => new(cs);

    [Fact]
    public void Sql_login_sets_user_and_password_and_disables_integrated_security()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin), "s3cret");
        var b = Parse(cs);
        Assert.Equal("app", b.UserID);
        Assert.Equal("s3cret", b.Password);
        Assert.False(b.IntegratedSecurity);
        Assert.Equal(SqlAuthenticationMethod.SqlPassword, b.Authentication);
    }

    [Fact]
    public void Windows_integrated_sets_integrated_security_and_no_password()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.WindowsIntegrated), null);
        var b = Parse(cs);
        Assert.True(b.IntegratedSecurity);
        Assert.True(string.IsNullOrEmpty(b.Password));
    }

    [Fact]
    public void Entra_interactive_sets_the_active_directory_interactive_method()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.EntraIdInteractive), null);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryInteractive, Parse(cs).Authentication);
    }

    [Fact]
    public void Port_is_appended_to_the_data_source_when_present()
    {
        var cs = SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin) with { Port = 14330 }, "x");
        Assert.Equal("srv01,14330", Parse(cs).DataSource);
    }

    [Fact]
    public void Sql_login_without_password_throws()
    {
        Assert.Throws<ArgumentException>(
            () => SqlServerConnectionFactory.BuildConnectionString(Base(AuthMode.SqlLogin), null));
    }
}
