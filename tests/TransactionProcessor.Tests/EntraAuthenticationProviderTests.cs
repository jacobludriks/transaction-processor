using Microsoft.Data.SqlClient;

namespace TransactionProcessor.Tests;

/// <summary>
/// Microsoft.Data.SqlClient 7.x ships Entra ID authentication in a separate package. Without it the
/// managed-identity connection in SqlConnectionStrings fails at login with "Cannot find an authentication
/// provider", which only shows up against a real Azure SQL server. This pins that the provider is present.
/// </summary>
public class EntraAuthenticationProviderTests
{
    [Fact]
    public void Managed_identity_authentication_provider_is_available()
    {
        // Referencing the library type makes sure the shared assembly (and its dependencies) are loaded.
        _ = SqlConnectionStrings.ForManagedIdentity("example.database.windows.net", "db", "00000000-0000-0000-0000-000000000000");

        var provider = SqlAuthenticationProvider.GetProvider(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity);

        Assert.NotNull(provider);
    }
}
