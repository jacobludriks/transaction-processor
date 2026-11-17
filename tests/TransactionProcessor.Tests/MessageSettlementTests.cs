using TransactionProcessor.Host;
using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

/// <summary>What the receive loop does to a message for each handler outcome, observed through the settlement seam.</summary>
public class MessageSettlementTests
{
    private static async Task<FakeSettlement> ProcessAsync(HandlerOutcome outcome)
        => await ProcessAsync((_, _) => Task.FromResult(outcome));

    private static async Task<FakeSettlement> ProcessAsync(Func<string, CancellationToken, Task<HandlerOutcome>> handle)
    {
        var settlement = new FakeSettlement();
        await new MessageProcessor(handle).ProcessAsync("<xml/>", settlement);
        return settlement;
    }

    [Fact]
    public async Task Sent_to_queue_completes_the_message()
        => Assert.Equal(["complete"], (await ProcessAsync(new HandlerOutcome.SendToQueue())).Actions);

    [Fact]
    public async Task Sent_to_event_hub_completes_the_message()
        => Assert.Equal(["complete"], (await ProcessAsync(new HandlerOutcome.SendToEventHub())).Actions);

    [Theory]
    [InlineData(DeadLetterReasons.InvalidXml)]
    [InlineData(DeadLetterReasons.ItemNotFound)]
    public async Task Dead_letter_outcome_dead_letters_the_message_with_the_handlers_reason(string reason)
        => Assert.Equal([$"dead-letter:{reason}"], (await ProcessAsync(new HandlerOutcome.DeadLetter(reason))).Actions);

    [Fact]
    public async Task Retry_outcome_abandons_the_message_so_it_is_redelivered()
        => Assert.Equal(["abandon"], (await ProcessAsync(new HandlerOutcome.Retry())).Actions);

    [Fact]
    public async Task The_handler_receives_the_message_body()
    {
        string? seen = null;
        var settlement = new FakeSettlement();
        await new MessageProcessor((body, _) => { seen = body; return Task.FromResult<HandlerOutcome>(new HandlerOutcome.SendToQueue()); })
            .ProcessAsync("<transaction/>", settlement);
        Assert.Equal("<transaction/>", seen);
    }

    [Fact]
    public async Task An_unexpected_handler_exception_abandons_the_message_rather_than_losing_it()
    {
        var settlement = new FakeSettlement();
        var processor = new MessageProcessor((_, _) => throw new InvalidOperationException("boom"));

        // The exception still surfaces so the host's error handler can log it.
        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync("<xml/>", settlement));

        Assert.Equal(["abandon"], settlement.Actions);
    }
}
