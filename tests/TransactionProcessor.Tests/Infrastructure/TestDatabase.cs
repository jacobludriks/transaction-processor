using Microsoft.Data.SqlClient;

namespace TransactionProcessor.Tests.Infrastructure;

/// <summary>A throwaway database on the shared container, with the Item and Sku tables created. Dropped on dispose.</summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _name;
    private readonly string _masterConnectionString;

    internal TestDatabase(string name, string connectionString, string masterConnectionString)
    {
        _name = name;
        ConnectionString = connectionString;
        _masterConnectionString = masterConnectionString;
    }

    public string ConnectionString { get; }

    internal Task CreateSchemaAsync() => ExecuteAsync(ReferenceDataSchema.CreateTablesSql);

    public Task AddItemAsync(long itemId, string externalItemId) =>
        ExecuteAsync("INSERT INTO dbo.Item (ItemId, ExternalItemId) VALUES (@id, @ext)",
            new SqlParameter("@id", itemId), new SqlParameter("@ext", externalItemId));

    public Task AddSkuAsync(string skuId) =>
        ExecuteAsync("INSERT INTO dbo.Sku (SkuId) VALUES (@id)", new SqlParameter("@id", skuId));

    private async Task ExecuteAsync(string sql, params SqlParameter[] parameters)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"ALTER DATABASE [{_name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_name}]";
        await command.ExecuteNonQueryAsync();
    }
}
