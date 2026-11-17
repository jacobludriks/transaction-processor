using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class RecognisedSkuTests(WslcSqlServerFixture sqlServer)
{
    [Fact]
    public async Task Item_found_and_sku_recognised_is_sent_to_the_queue_with_the_decoded_message_plus_ExternalItemId()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync(SampleTransaction.SkuId);
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.IsType<HandlerOutcome.SendToQueue>(outcome);
        var json = Assert.Single(queue.Sent);
        SampleTransaction.AssertJson(json, expectedExternalItemId: "EXT-ITEM-4711");
        Assert.Empty(eventHub.Sent);
    }
}
