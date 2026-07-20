using Microsoft.Data.SqlClient;
using SmartIndexManager.Core.Provider;

namespace SmartIndexManager.Providers.SqlServer.Connection;

public static class SqlServerConnectionFactory
{
    public static string BuildConnectionString(ConnectionRequest request, string? password)
    {
        var b = new SqlConnectionStringBuilder
        {
            DataSource = request.Port is int p ? $"{request.Server},{p}" : request.Server,
            Encrypt = request.Encrypt,
            TrustServerCertificate = request.TrustServerCertificate,
            ApplicationName = request.ApplicationName,
            ConnectTimeout = request.ConnectTimeoutSeconds
        };

        switch (request.Auth)
        {
            case AuthMode.WindowsIntegrated:
                b.IntegratedSecurity = true;
                break;

            case AuthMode.SqlLogin:
                if (string.IsNullOrEmpty(request.Login))
                    throw new ArgumentException("SQL login requires a user name.", nameof(request));
                if (string.IsNullOrEmpty(password))
                    throw new ArgumentException("SQL login requires a password.", nameof(password));
                b.Authentication = SqlAuthenticationMethod.SqlPassword;
                b.UserID = request.Login;
                b.Password = password;
                break;

            case AuthMode.EntraIdInteractive:
                b.Authentication = SqlAuthenticationMethod.ActiveDirectoryInteractive;
                if (!string.IsNullOrEmpty(request.Login)) b.UserID = request.Login;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(request), request.Auth, "Unknown auth mode.");
        }

        return b.ConnectionString;
    }
}
