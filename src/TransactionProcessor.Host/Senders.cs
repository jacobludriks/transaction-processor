using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.ServiceBus;

namespace TransactionProcessor.Host;

/// <summary>Sends the handler's JSON output to the Service Bus queue.</summary>
public sealed class ServiceBusQueueSender(ServiceBusClient client, string queueName) : IQueueSender, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(queueName);

    public Task SendAsync(string json, CancellationToken cancellationToken = default) =>
        _sender.SendMessageAsync(new ServiceBusMessage(json) { ContentType = "application/json" }, cancellationToken);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

/// <summary>Sends the handler's JSON output to the event hub.</summary>
public sealed class EventHubProducerSender(string fullyQualifiedNamespace, string eventHubName, TokenCredential credential)
    : IEventHubSender, IAsyncDisposable
{
    private readonly EventHubProducerClient _producer = new(fullyQualifiedNamespace, eventHubName, credential);

    // No partition key: the service spreads events across partitions.
    public Task SendAsync(string json, CancellationToken cancellationToken = default) =>
        _producer.SendAsync([new EventData(json) { ContentType = "application/json" }], cancellationToken);

    public ValueTask DisposeAsync() => _producer.DisposeAsync();
}
