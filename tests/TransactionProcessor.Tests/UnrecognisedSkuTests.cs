using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class UnrecognisedSkuTests(WslcSqlServerFixture sqlServer)
{
    [Fact]
    public async Task Item_found_and_sku_unrecognised_is_sent_to_the_event_hub_with_the_decoded_message_plus_ExternalItemId()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        await database.AddItemAsync(SampleTransaction.ItemId, "EXT-ITEM-4711");
        await database.AddSkuAsync("SKU-SOMETHING-ELSE");
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        var outcome = await handler.HandleAsync(SampleTransaction.Xml());

        Assert.IsType<HandlerOutcome.SendToEventHub>(outcome);
        var json = Assert.Single(eventHub.Sent);
        SampleTransaction.AssertJson(json, expectedExternalItemId: "EXT-ITEM-4711");
        Assert.Empty(queue.Sent);
    }
}
