namespace TransactionProcessor.Host;

/// <summary>
/// What can be done to one received message. The seam between the receive loop and Service Bus: the real
/// implementation settles a <c>ServiceBusReceivedMessage</c>, tests record the calls.
/// </summary>
public interface IMessageSettlement
{
    /// <summary>The message was fully dealt with: remove it from the subscription.</summary>
    Task CompleteAsync(CancellationToken cancellationToken = default);

    /// <summary>The message can never succeed: move it to the dead-letter queue with this reason.</summary>
    Task DeadLetterAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Give the message back so Service Bus redelivers it.</summary>
    Task AbandonAsync(CancellationToken cancellationToken = default);
}
