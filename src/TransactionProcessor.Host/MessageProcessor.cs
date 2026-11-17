namespace TransactionProcessor.Host;

/// <summary>
/// Hands a message body to the handler and applies the handler's outcome to the message. It decides nothing
/// about the message itself: routing and lookups stay in the handler.
/// </summary>
public sealed class MessageProcessor(Func<string, CancellationToken, Task<HandlerOutcome>> handle)
{
    public async Task ProcessAsync(string body, IMessageSettlement settlement, CancellationToken cancellationToken = default)
    {
        HandlerOutcome outcome;
        try
        {
            outcome = await handle(body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The handler turns every expected failure into an outcome. Anything else is a bug: give the
            // message back rather than lose it. Service Bus dead-letters it once the delivery count is exhausted.
            await settlement.AbandonAsync(cancellationToken);
            throw;
        }

        switch (outcome)
        {
            // The handler has already sent the output, so the message is complete.
            case HandlerOutcome.SendToQueue or HandlerOutcome.SendToEventHub:
                await settlement.CompleteAsync(cancellationToken);
                break;
            case HandlerOutcome.DeadLetter deadLetter:
                await settlement.DeadLetterAsync(deadLetter.Reason, cancellationToken);
                break;
            case HandlerOutcome.Retry:
                await settlement.AbandonAsync(cancellationToken);
                break;
        }
    }
}
