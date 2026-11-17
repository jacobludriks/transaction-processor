namespace TransactionProcessor;

/// <summary>What the receive loop should do with a message once the handler has dealt with it.</summary>
public abstract record HandlerOutcome
{
    private HandlerOutcome() { }

    /// <summary>The SKU was recognised and the output was sent to the queue: complete the message.</summary>
    public sealed record SendToQueue : HandlerOutcome;

    /// <summary>The SKU was not recognised and the output was sent to the event hub: complete the message.</summary>
    public sealed record SendToEventHub : HandlerOutcome;

    /// <summary>The message can never succeed: dead-letter it with a machine-readable reason.</summary>
    public sealed record DeadLetter(string Reason) : HandlerOutcome;

    /// <summary>A transient failure occurred: abandon the message so Service Bus redelivers it.</summary>
    public sealed record Retry : HandlerOutcome;
}
