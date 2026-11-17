using System.Data;
using Microsoft.Data.SqlClient;
using TransactionProcessor;

namespace DataSeeder;

/// <summary>Counts of rows in the reference tables after a seed run, and how many that run added.</summary>
public sealed record SeedResult(long ItemRows, long ItemRowsAdded, long SkuRows, long SkuRowsAdded);

/// <summary>
/// Fills the Item and Sku tables using the shared key scheme (<see cref="ReferenceDataKeys"/>) and table
/// definitions (<see cref="ReferenceDataSchema"/>). Safe to run repeatedly: tables are created only if missing,
/// and only the missing keys are loaded, so a second run adds nothing and a partial load is completed.
/// </summary>
public static class ReferenceDataSeeder
{
    private const int BatchSize = 50_000;

    private sealed record TableSpec(
        string Table,
        string KeyColumn,
        string CreateSql,
        string[] Columns,
        Func<long, object[]> Row,
        string ReadStateSql,
        Func<object?, long> KeyNumber);

    private static readonly TableSpec ItemTable = new(
        "dbo.Item", "ItemId", ReferenceDataSchema.CreateItemTableSql,
        ["ItemId", "ExternalItemId", "Name", "Category"],
        id => [id, ReferenceDataKeys.ExternalItemId(id), "Item " + id, "Category-" + (id % 50)],
        "SELECT COUNT_BIG(*), MAX(ItemId) FROM dbo.Item",
        max => max is null ? 0 : Convert.ToInt64(max));

    private static readonly TableSpec SkuTable = new(
        "dbo.Sku", "SkuId", ReferenceDataSchema.CreateSkuTableSql,
        ["SkuId", "Description", "Brand"],
        n => [ReferenceDataKeys.SkuId((int)n), "SKU description " + n, "Brand-" + (n % 100)],
        "SELECT COUNT_BIG(*), MAX(SkuId) FROM dbo.Sku",
        // Seeded keys are "SKU-" plus digits and sort in numeric order, so the highest key is the highest number.
        // A key that does not parse gives -1, which never equals the row count, so it is treated as "not a clean prefix".
        max => max is null ? 0
            : max is string text && text.StartsWith("SKU-", StringComparison.Ordinal)
              && long.TryParse(text.AsSpan(4), out var parsed) ? parsed : -1);

    public static async Task<SeedResult> SeedAsync(
        string connectionString, int itemCount, int skuCount, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        var items = await SeedTableAsync(connection, ItemTable, itemCount, cancellationToken);
        var skus = await SeedTableAsync(connection, SkuTable, skuCount, cancellationToken);
        return new SeedResult(items.Rows, items.Added, skus.Rows, skus.Added);
    }

    private static async Task<(long Rows, long Added)> SeedTableAsync(
        SqlConnection connection, TableSpec table, long target, CancellationToken ct)
    {
        await ExecuteAsync(connection,
            $"IF OBJECT_ID(N'{table.Table}', N'U') IS NULL BEGIN {table.CreateSql} END", ct);

        var (count, max) = await ReadStateAsync(connection, table.ReadStateSql, ct);
        var maxKey = table.KeyNumber(max);
        var added = 0L;
        if (count < target)
        {
            if (count == maxKey)
            {
                // Empty, or a contiguous 1..count prefix (what an interrupted ordered load leaves behind): carry on.
                await BulkLoadAsync(connection, table, table.Table, count + 1, target, ct);
                added = target - count;
            }
            else
            {
                // Gaps or foreign keys present: load the full range into a staging table and insert what is missing.
                var cols = string.Join(", ", table.Columns);
                await ExecuteAsync(connection, $"SELECT TOP 0 {cols} INTO #stage FROM {table.Table}", ct);
                await BulkLoadAsync(connection, table, "#stage", 1, target, ct);
                added = await ExecuteAsync(connection,
                    $"INSERT INTO {table.Table} ({cols}) SELECT {cols} FROM #stage s " +
                    $"WHERE NOT EXISTS (SELECT 1 FROM {table.Table} t WHERE t.{table.KeyColumn} = s.{table.KeyColumn}); " +
                    "DROP TABLE #stage;", ct);
            }
        }
        return (count + added, added);
    }

    /// <summary>Streams rows first..last (in key order) into the destination in batches, one commit per batch.</summary>
    private static async Task BulkLoadAsync(
        SqlConnection connection, TableSpec table, string destination, long first, long last, CancellationToken ct)
    {
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.TableLock, externalTransaction: null)
        {
            DestinationTableName = destination,
            BulkCopyTimeout = 0,
        };
        var batch = new DataTable();
        var sample = table.Row(first);
        for (var i = 0; i < table.Columns.Length; i++)
        {
            batch.Columns.Add(table.Columns[i], sample[i].GetType());
            bulk.ColumnMappings.Add(table.Columns[i], table.Columns[i]);
        }
        if (destination == table.Table)
            bulk.ColumnOrderHints.Add(table.KeyColumn, SortOrder.Ascending);

        for (var start = first; start <= last; start += BatchSize)
        {
            var end = Math.Min(start + BatchSize - 1, last);
            batch.Clear();
            for (var key = start; key <= end; key++) batch.Rows.Add(table.Row(key));
            await bulk.WriteToServerAsync(batch, ct);
        }
    }

    private static async Task<(long Count, object? Max)> ReadStateAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetValue(1));
    }

    private static async Task<int> ExecuteAsync(SqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = 0;
        return await command.ExecuteNonQueryAsync(ct);
    }
}
