using TransactionProcessor;
using TransactionProcessor.Tests.Infrastructure;

namespace LoadGenerator.Tests;

/// <summary>
/// The functional set, judged by the real handler: each labelled message is dead-lettered with the reason
/// it says it expects. The first three never reach the database, so they use an unreachable connection string.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class FunctionalSetTests(WslcSqlServerFixture sqlServer)
{
    private static HandlerOutcome Handle(string connectionString, GeneratedMessage message) =>
        new TransactionHandler(connectionString, new FakeQueueSender(), new FakeEventHubSender())
            .HandleAsync(message.Body).GetAwaiter().GetResult();

    [Fact]
    public void The_default_functional_set_is_one_malformed_and_one_missing_itemId_message_each_labelled()
    {
        var set = new MessageGenerator(1).FunctionalSet();

        Assert.Equal(["functional-malformed-xml", "functional-missing-itemId"], set.Select(m => m.Label));
        Assert.Equal(["InvalidXml", "MissingItemId"], set.Select(m => m.ExpectedDeadLetterReason));
    }

    [Fact]
    public void The_optional_messages_are_added_on_request()
    {
        var set = new MessageGenerator(1).FunctionalSet(includeMissingSku: true, includeUnknownItem: true);

        Assert.Equal(
            ["functional-malformed-xml", "functional-missing-itemId", "functional-missing-skuId", "functional-unknown-itemId"],
            set.Select(m => m.Label));
    }

    [Fact]
    public void The_handler_dead_letters_each_message_that_never_needs_the_database_with_its_expected_reason()
    {
        var unreachable = TestDatabase.UnreachableConnectionString();
        var set = new MessageGenerator(1).FunctionalSet(includeMissingSku: true);

        foreach (var message in set)
            Assert.Equal(new HandlerOutcome.DeadLetter(message.ExpectedDeadLetterReason!), Handle(unreachable, message));
    }

    [Fact]
    public async Task The_handler_dead_letters_the_unknown_item_message_with_ItemNotFound_when_the_item_table_has_no_such_row()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        // A seeded-looking database: items and SKUs exist, but nothing above the item count.
        await database.AddItemAsync(1, ReferenceDataKeys.ExternalItemId(1));
        var message = new MessageGenerator(1).FunctionalSet(includeUnknownItem: true).Single(m => m.Label == "functional-unknown-itemId");

        var outcome = Handle(database.ConnectionString, message);

        Assert.Equal(new HandlerOutcome.DeadLetter("ItemNotFound"), outcome);
    }

    [Fact]
    public async Task Load_messages_are_routed_by_the_handler_with_unmatched_skus_to_the_event_hub()
    {
        await using var database = await sqlServer.CreateDatabaseAsync();
        var generator = new MessageGenerator(5, unmatchedRatio: 0.5);
        var messages = Enumerable.Range(0, 20).Select(_ => generator.NextLoadMessage()).ToList();
        var queue = new FakeQueueSender();
        var eventHub = new FakeEventHubSender();
        var handler = new TransactionHandler(database.ConnectionString, queue, eventHub);

        // Seed exactly the keys these messages carry, then check the handler's routing agrees with the labels.
        foreach (var message in messages)
        {
            var xml = System.Xml.Linq.XDocument.Parse(message.Body).Root!;
            var itemId = long.Parse(xml.Element("itemId")!.Value);
            var sku = xml.Element("skuId")!.Value;
            try { await database.AddItemAsync(itemId, ReferenceDataKeys.ExternalItemId(itemId)); } catch (Microsoft.Data.SqlClient.SqlException) { }
            if (message.Label == "load-matched-sku")
                try { await database.AddSkuAsync(sku); } catch (Microsoft.Data.SqlClient.SqlException) { }
        }
        var outcomes = new List<HandlerOutcome>();
        foreach (var message in messages) outcomes.Add(await handler.HandleAsync(message.Body));

        for (var i = 0; i < messages.Count; i++)
            Assert.IsType(messages[i].Label == "load-matched-sku" ? typeof(HandlerOutcome.SendToQueue) : typeof(HandlerOutcome.SendToEventHub), outcomes[i]);
    }
}
