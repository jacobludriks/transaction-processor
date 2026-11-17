using Microsoft.Data.SqlClient;

namespace TransactionProcessor.Host;

public static class SqlConnectionStrings
{
    /// <summary>Connection string with no password: Entra authentication as the managed identity with this client ID.</summary>
    public static string ForManagedIdentity(ProcessorOptions options) =>
        new SqlConnectionStringBuilder
        {
            DataSource = options.SqlServer,
            InitialCatalog = options.SqlDatabase,
            Authentication = SqlAuthenticationMethod.ActiveDirectoryManagedIdentity,
            UserID = options.ManagedIdentityClientId,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
        }.ConnectionString;
}
