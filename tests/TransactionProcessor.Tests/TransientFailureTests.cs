using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class TransientFailureTests(WslcSqlServerFixture sqlServer)
{
    [Fact]
    public async Task Item_lookup_against_an_unreachable_database_is_retried()
    {
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(TestDatabase.UnreachableConnectionString(), queue, eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.IsType<HandlerOutcome.Retry>(outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Sku_lookup_against_a_failing_database_is_retried()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.DropSkuTableAsync(); // the item lookup succeeds, the SKU lookup raises a database error
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.IsType<HandlerOutcome.Retry>(outcome);
        Assert.Empty(queue.Sent);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Failed_send_to_the_queue_is_retried()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, new FailingQueueSender(), eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.IsType<HandlerOutcome.Retry>(outcome);
        Assert.Empty(eventHub.Sent);
    }

    [Fact]
    public async Task Failed_send_to_the_event_hub_is_retried()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        var queue = new FakeQueueSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, new FailingEventHubSender());

        var outcome = await handler.HandleAsync(SampleTransaction.Xml(skuId: "SKU-UNKNOWN"));

        Assert.IsType<HandlerOutcome.Retry>(outcome);
        Assert.Empty(queue.Sent);
    }
}
