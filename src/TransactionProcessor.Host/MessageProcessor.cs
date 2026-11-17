namespace TransactionProcessor.Host;

/// <summary>
/// Hands a message body to the handler and applies the handler's outcome to the message. It decides nothing
/// about the message itself: routing and lookups stay in the handler. It also records the outcome (and, for a
/// sent message, the enqueue-to-output-sent latency) as metrics; that is telemetry only.
/// </summary>
public sealed class MessageProcessor(
    Func<string, CancellationToken, Task<HandlerOutcome>> handle,
    ProcessorMetrics? metrics = null,
    TimeProvider? timeProvider = null)
{
    private readonly ProcessorMetrics _metrics = metrics ?? new ProcessorMetrics();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <param name="enqueuedTime">When Service Bus enqueued the message. Latency is measured from here.</param>
    public async Task ProcessAsync(
        string body, IMessageSettlement settlement, CancellationToken cancellationToken = default, DateTimeOffset? enqueuedTime = null)
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
            _metrics.Abandoned("unhandled_exception");
            await settlement.AbandonAsync(cancellationToken);
            throw;
        }

        // The handler returns once its output send has completed, so this is the end of the latency measurement.
        var outputSentAt = _time.GetUtcNow();

        switch (outcome)
        {
            // The handler has already sent the output, so the message is complete.
            case HandlerOutcome.SendToQueue:
                if (enqueuedTime is { } queued) _metrics.SentToQueue((outputSentAt - queued).TotalSeconds);
                else _metrics.SentToQueue();
                await settlement.CompleteAsync(cancellationToken);
                break;
            case HandlerOutcome.SendToEventHub:
                if (enqueuedTime is { } queuedForHub) _metrics.SentToEventHub((outputSentAt - queuedForHub).TotalSeconds);
                else _metrics.SentToEventHub();
                await settlement.CompleteAsync(cancellationToken);
                break;
            case HandlerOutcome.DeadLetter deadLetter:
                _metrics.DeadLettered(deadLetter.Reason);
                await settlement.DeadLetterAsync(deadLetter.Reason, cancellationToken);
                break;
            case HandlerOutcome.Retry:
                _metrics.Abandoned("transient_failure");
                await settlement.AbandonAsync(cancellationToken);
                break;
        }
    }
}
