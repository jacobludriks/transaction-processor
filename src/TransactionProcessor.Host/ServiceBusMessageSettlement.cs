using Azure.Messaging.ServiceBus;

namespace TransactionProcessor.Host;

/// <summary>Settles one received Service Bus message.</summary>
public sealed class ServiceBusMessageSettlement(ProcessMessageEventArgs args) : IMessageSettlement
{
    public Task CompleteAsync(CancellationToken cancellationToken = default) =>
        args.CompleteMessageAsync(args.Message, cancellationToken);

    public Task DeadLetterAsync(string reason, CancellationToken cancellationToken = default) =>
        args.DeadLetterMessageAsync(args.Message, deadLetterReason: reason, cancellationToken: cancellationToken);

    public Task AbandonAsync(CancellationToken cancellationToken = default) =>
        args.AbandonMessageAsync(args.Message, cancellationToken: cancellationToken);
}
