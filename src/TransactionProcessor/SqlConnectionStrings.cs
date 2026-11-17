using Microsoft.Data.SqlClient;

namespace TransactionProcessor;

/// <summary>Builds the SQL connection strings used by the processor host and the data seeder.</summary>
public static class SqlConnectionStrings
{
    /// <summary>
    /// Connection string with no password: Entra authentication as the user-assigned managed identity with this
    /// client ID. <paramref name="connectTimeoutSeconds"/> is left at the driver's default when null.
    /// </summary>
    public static string ForManagedIdentity(string server, string database, string managedIdentityClientId, int? connectTimeoutSeconds = null)
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database,
            Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity,
            UserID = managedIdentityClientId, // the managed identity's client ID; no secret is involved
            Encrypt = SqlConnectionEncryptOption.Mandatory,
        };
        if (connectTimeoutSeconds is { } timeout) builder.ConnectTimeout = timeout;
        return builder.ConnectionString;
    }
}
