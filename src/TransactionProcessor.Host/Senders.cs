using Azure.Core;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.ServiceBus;

namespace TransactionProcessor.Host;

/// <summary>
/// Sends the handler's JSON output to the Service Bus queue. Concurrent sends are combined into batch sends by a
/// <see cref="SendBatcher"/>. The sender owns its own <see cref="ServiceBusClient"/>, and so its own AMQP connection,
/// so output sends do not share a connection with the receive and settle traffic of the receive loop.
/// </summary>
public sealed class ServiceBusQueueSender : IQueueSender, IAsyncDisposable
{
    private readonly ServiceBusClient _client;
    private readonly ServiceBusSender _sender;
    private readonly SendBatcher _batcher;

    public ServiceBusQueueSender(string fullyQualifiedNamespace, string queueName, TokenCredential credential, ProcessorMetrics? metrics = null)
    {
        _client = new ServiceBusClient(fullyQualifiedNamespace, credential);
        _sender = _client.CreateSender(queueName);
        _batcher = new SendBatcher(SendBatchAsync, batchSent: size => metrics?.OutputBatchSent(OutputDestinations.Queue, size));
    }

    public Task SendAsync(string json, CancellationToken cancellationToken = default) => _batcher.SendAsync(json, cancellationToken);

    private async Task SendBatchAsync(IReadOnlyList<string> payloads, CancellationToken cancellationToken)
    {
        var next = 0;
        while (next < payloads.Count)
        {
            using var batch = await _sender.CreateMessageBatchAsync(cancellationToken);
            while (next < payloads.Count && batch.TryAddMessage(ToMessage(payloads[next]))) next++;
            if (batch.Count == 0)
            {
                // One message too large for a batch on its own: send it singly so the service's size error surfaces.
                await _sender.SendMessageAsync(ToMessage(payloads[next++]), cancellationToken);
                continue;
            }
            await _sender.SendMessagesAsync(batch, cancellationToken);
        }
    }

    private static ServiceBusMessage ToMessage(string json) => new(json) { ContentType = "application/json" };

    public async ValueTask DisposeAsync()
    {
        await _batcher.DisposeAsync();
        await _sender.DisposeAsync();
        await _client.DisposeAsync();
    }
}

/// <summary>Sends the handler's JSON output to the event hub, combining concurrent sends into batch sends.</summary>
public sealed class EventHubProducerSender : IEventHubSender, IAsyncDisposable
{
    private readonly EventHubProducerClient _producer;
    private readonly SendBatcher _batcher;

    public EventHubProducerSender(string fullyQualifiedNamespace, string eventHubName, TokenCredential credential, ProcessorMetrics? metrics = null)
    {
        _producer = new EventHubProducerClient(fullyQualifiedNamespace, eventHubName, credential);
        _batcher = new SendBatcher(SendBatchAsync, batchSent: size => metrics?.OutputBatchSent(OutputDestinations.EventHub, size));
    }

    public Task SendAsync(string json, CancellationToken cancellationToken = default) => _batcher.SendAsync(json, cancellationToken);

    // No partition key: the service spreads batches across partitions.
    private async Task SendBatchAsync(IReadOnlyList<string> payloads, CancellationToken cancellationToken)
    {
        var next = 0;
        while (next < payloads.Count)
        {
            using var batch = await _producer.CreateBatchAsync(cancellationToken);
            while (next < payloads.Count && batch.TryAdd(ToEvent(payloads[next]))) next++;
            if (batch.Count == 0)
            {
                // One event too large for a batch on its own: send it singly so the service's size error surfaces.
                await _producer.SendAsync([ToEvent(payloads[next++])], cancellationToken);
                continue;
            }
            await _producer.SendAsync(batch, cancellationToken);
        }
    }

    private static EventData ToEvent(string json) => new(json) { ContentType = "application/json" };

    public async ValueTask DisposeAsync()
    {
        await _batcher.DisposeAsync();
        await _producer.DisposeAsync();
    }
}

/// <summary>Values of the <c>destination</c> tag on the host's metrics.</summary>
public static class OutputDestinations
{
    public const string Queue = "queue";
    public const string EventHub = "event_hub";
}
