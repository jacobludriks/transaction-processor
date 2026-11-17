using Microsoft.Data.SqlClient;
using DataSeeder;
using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class SeederTests(WslcSqlServerFixture sqlServer)
{
    [Fact]
    public async Task Seeding_an_empty_database_creates_the_requested_items_and_skus_using_the_shared_key_scheme()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();

        var result = await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 120, skuCount: 80);

        Assert.Equal(new SeedResult(120, 120, 80, 80), result);
        Assert.Equal(120, await Scalar<int>(database, "SELECT COUNT(*) FROM dbo.Item"));
        Assert.Equal(80, await Scalar<int>(database, "SELECT COUNT(*) FROM dbo.Sku"));
        Assert.Equal(1L, await Scalar<long>(database, "SELECT MIN(ItemId) FROM dbo.Item"));
        Assert.Equal(120L, await Scalar<long>(database, "SELECT MAX(ItemId) FROM dbo.Item"));
        // ExternalItemId is derivable from the item key through the shared scheme (checked for every row).
        Assert.Equal(0, await Scalar<int>(database,
            "SELECT COUNT(*) FROM dbo.Item WHERE ExternalItemId <> N'EXT-' + CAST(ItemId AS NVARCHAR(20))"));
        Assert.Equal(ReferenceDataKeys.ExternalItemId(77), await Scalar<string>(database,
            "SELECT ExternalItemId FROM dbo.Item WHERE ItemId = 77"));
        Assert.Equal(ReferenceDataKeys.SkuId(1), await Scalar<string>(database, "SELECT MIN(SkuId) FROM dbo.Sku"));
        Assert.Equal(ReferenceDataKeys.SkuId(80), await Scalar<string>(database, "SELECT MAX(SkuId) FROM dbo.Sku"));
        Assert.Equal(1, await Scalar<int>(database,
            $"SELECT COUNT(*) FROM dbo.Sku WHERE SkuId = N'{ReferenceDataKeys.SkuId(42)}'"));
        // Keys the load generator treats as unrecognised are never seeded.
        Assert.Equal(0, await Scalar<int>(database,
            $"SELECT COUNT(*) FROM dbo.Sku WHERE SkuId = N'{ReferenceDataKeys.SkuId(ReferenceDataKeys.UnknownSkuFirst)}'"));
    }

    [Fact]
    public async Task Seeding_a_database_without_the_tables_creates_them()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await Execute(database, "DROP TABLE dbo.Item; DROP TABLE dbo.Sku;");

        var result = await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 10, skuCount: 10);

        Assert.Equal(new SeedResult(10, 10, 10, 10), result);
        Assert.Equal(ReferenceDataKeys.ExternalItemId(10), await Scalar<string>(database,
            "SELECT ExternalItemId FROM dbo.Item WHERE ItemId = 10"));
    }

    [Fact]
    public async Task Running_the_seeder_again_on_a_seeded_database_adds_nothing_and_does_not_fail()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 120, skuCount: 80);

        var second = await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 120, skuCount: 80);

        Assert.Equal(new SeedResult(120, 0, 80, 0), second);
        Assert.Equal(120, await Scalar<int>(database, "SELECT COUNT(*) FROM dbo.Item"));
        Assert.Equal(80, await Scalar<int>(database, "SELECT COUNT(*) FROM dbo.Sku"));
    }

    [Fact]
    public async Task A_partially_loaded_database_is_completed_without_duplicating_existing_rows()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        // An interrupted earlier run: only the first rows of each table made it in.
        for (var id = 1; id <= 30; id++) await database.AddItemAsync(id, ReferenceDataKeys.ExternalItemId(id));
        for (var n = 1; n <= 20; n++) await database.AddSkuAsync(ReferenceDataKeys.SkuId(n));

        var result = await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 120, skuCount: 80);

        Assert.Equal(new SeedResult(120, 90, 80, 60), result);
        Assert.Equal(120, await Scalar<int>(database, "SELECT COUNT(DISTINCT ItemId) FROM dbo.Item"));
        Assert.Equal(80, await Scalar<int>(database, "SELECT COUNT(DISTINCT SkuId) FROM dbo.Sku"));
        Assert.Equal(0, await Scalar<int>(database,
            "SELECT COUNT(*) FROM dbo.Item WHERE ExternalItemId <> N'EXT-' + CAST(ItemId AS NVARCHAR(20))"));
    }

    [Fact]
    public async Task A_load_with_gaps_in_the_keys_is_completed_by_filling_only_the_missing_keys()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        foreach (var id in new[] { 1, 2, 3, 50, 51, 100 }) await database.AddItemAsync(id, ReferenceDataKeys.ExternalItemId(id));
        foreach (var n in new[] { 1, 2, 40, 80 }) await database.AddSkuAsync(ReferenceDataKeys.SkuId(n));

        var result = await ReferenceDataSeeder.SeedAsync(database.ConnectionString, itemCount: 120, skuCount: 80);

        Assert.Equal(new SeedResult(120, 114, 80, 76), result);
        Assert.Equal(120, await Scalar<int>(database, "SELECT COUNT(DISTINCT ItemId) FROM dbo.Item"));
        Assert.Equal(80, await Scalar<int>(database, "SELECT COUNT(DISTINCT SkuId) FROM dbo.Sku"));
    }

    [Fact]
    public async Task The_command_line_seeds_using_a_plain_connection_string_and_reports_row_counts_and_elapsed_time()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var output = new StringWriter();

        var exitCode = await SeederCli.RunAsync(
            ["--connection-string", database.ConnectionString, "--items", "25", "--skus", "15"], output, TextWriter.Null);

        Assert.Equal(0, exitCode);
        Assert.Contains("Item rows: 25", output.ToString());
        Assert.Contains("Sku rows:  15", output.ToString());
        Assert.Contains("Elapsed:", output.ToString());
        Assert.Equal(25, await Scalar<int>(database, "SELECT COUNT(*) FROM dbo.Item"));
    }

    [Fact]
    public async Task The_command_line_rejects_missing_connection_details()
    {
        var error = new StringWriter();

        var exitCode = await SeederCli.RunAsync(["--server", "example.database.windows.net"], TextWriter.Null, error);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("--client-id", error.ToString());
    }

    private static async Task Execute(TestDatabase database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> Scalar<T>(TestDatabase database, string sql)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
