using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class PermanentFailureTests(WslcSqlServerFixture sqlServer)
{
    [Fact]
    public async Task Unparseable_xml_is_dead_lettered_with_reason_InvalidXml_and_nothing_is_sent()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync("<transaction><itemId>4711</itemId>");

        Assert.Equal(new HandlerOutcome.DeadLetter("InvalidXml"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Missing_itemId_is_dead_lettered_with_reason_MissingItemId_and_nothing_is_sent()
    {
        // The item and SKU exist, so only the missing key can explain the outcome.
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(WithoutElement(SampleTransaction.Xml(), "itemId"));

        Assert.Equal(new HandlerOutcome.DeadLetter("MissingItemId"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Missing_skuId_is_dead_lettered_with_reason_MissingSkuId_and_nothing_is_sent()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(WithoutElement(SampleTransaction.Xml(), "skuId"));

        Assert.Equal(new HandlerOutcome.DeadLetter("MissingSkuId"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_itemId_is_dead_lettered_with_reason_MissingItemId(string blank)
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(WithElementText(SampleTransaction.Xml(), "itemId", blank));

        Assert.Equal(new HandlerOutcome.DeadLetter("MissingItemId"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Non_numeric_itemId_is_dead_lettered_with_reason_InvalidXml()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var handler = new TransactionHandler(database.ConnectionString, new FakeQueueSender(), new FakeEventHubSender());

        var outcome = await handler.HandleAsync(WithElementText(SampleTransaction.Xml(), "itemId", "abc"));

        Assert.Equal(new HandlerOutcome.DeadLetter("InvalidXml"), outcome);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_skuId_is_dead_lettered_with_reason_MissingSkuId(string blank)
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(WithElementText(SampleTransaction.Xml(), "skuId", blank));

        Assert.Equal(new HandlerOutcome.DeadLetter("MissingSkuId"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Item_with_no_row_is_dead_lettered_with_reason_ItemNotFound_and_nothing_is_sent_or_written()
    {
        // The SKU is recognised, so only the missing item row can explain the outcome.
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId + 1, "EXT-OTHER-ITEM");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.Equal(new HandlerOutcome.DeadLetter("ItemNotFound"), outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
        Assert.Equal((1, 1), await CountRowsAsync(database));
    }

    private static async Task<(int Items, int Skus)> CountRowsAsync(TestDatabase database)
    {
        await using var connection = new SqlConnection(database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (SELECT COUNT(*) FROM dbo.Item), (SELECT COUNT(*) FROM dbo.Sku)";
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetInt32(1));
    }

    private static string WithElementText(string xml, string name, string text) =>
        Regex.Replace(xml, $"<{name}>.*?</{name}>", $"<{name}>{text}</{name}>");

    private static string WithoutElement(string xml, string name) =>
        Regex.Replace(xml, $"[ \t]*<{name}>.*?</{name}>\r?\n", "");
}
