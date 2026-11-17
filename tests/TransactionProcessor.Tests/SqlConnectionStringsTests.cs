using Microsoft.Data.SqlClient;

namespace TransactionProcessor.Tests;

public class SqlConnectionStringsTests
{
    [Fact]
    public void A_managed_identity_connection_string_names_the_identity_and_holds_no_password()
    {
        var text = SqlConnectionStrings.ForManagedIdentity("sql-abc.database.windows.net", "txproc", "11111111-2222-3333-4444-555555555555");

        var parsed = new SqlConnectionStringBuilder(text);
        Assert.Equal("sql-abc.database.windows.net", parsed.DataSource);
        Assert.Equal("txproc", parsed.InitialCatalog);
        Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, parsed.Authentication);
        Assert.Equal("11111111-2222-3333-4444-555555555555", parsed.UserID);
        Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
        Assert.Equal("", parsed.Password);
    }

    [Fact]
    public void The_connect_timeout_is_only_set_when_asked_for()
    {
        const string id = "11111111-2222-3333-4444-555555555555";

        Assert.Equal(new SqlConnectionStringBuilder().ConnectTimeout,
            new SqlConnectionStringBuilder(SqlConnectionStrings.ForManagedIdentity("s", "d", id)).ConnectTimeout);
        Assert.Equal(60,
            new SqlConnectionStringBuilder(SqlConnectionStrings.ForManagedIdentity("s", "d", id, connectTimeoutSeconds: 60)).ConnectTimeout);
    }
}
