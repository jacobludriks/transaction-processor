using Azure.Core;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace LoadGenerator;

/// <summary>Publishes to a Service Bus topic, in batches, one sender per instance.</summary>
public sealed class ServiceBusSink(ServiceBusSender sender, TimeProvider? time = null) : IMessageSink
{
    /// <summary>Application property holding the UTC time the message was handed to Service Bus, ISO 8601 round-trip format.</summary>
    public const string SentAtProperty = "generatedAtUtc";
    public const string LabelProperty = "label";
    public const string ExpectedReasonProperty = "expectedDeadLetterReason";

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>
    /// Creates a client for the namespace, authenticating with <see cref="DefaultAzureCredential"/>: the managed
    /// identity picked by <paramref name="clientId"/> on the VM, or the developer's own sign-in when it is null.
    /// There are no connection strings or keys.
    /// </summary>
    public static ServiceBusClient CreateClient(string fullyQualifiedNamespace, string? clientId)
    {
        TokenCredential credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { ManagedIdentityClientId = clientId });
        return new ServiceBusClient(fullyQualifiedNamespace, credential, new ServiceBusClientOptions { TransportType = ServiceBusTransportType.AmqpTcp });
    }

    public async Task SendAsync(IReadOnlyList<GeneratedMessage> messages, CancellationToken cancellationToken = default)
    {
        var batch = await sender.CreateMessageBatchAsync(cancellationToken);
        try
        {
            foreach (var message in messages)
            {
                var outbound = ToServiceBusMessage(message);
                if (batch.TryAddMessage(outbound)) continue;

                // The batch is full: send it and start another one.
                if (batch.Count > 0) await sender.SendMessagesAsync(batch, cancellationToken);
                batch.Dispose();
                batch = await sender.CreateMessageBatchAsync(cancellationToken);
                if (!batch.TryAddMessage(outbound))
                    throw new InvalidOperationException("A single message is too large for a Service Bus batch.");
            }
            if (batch.Count > 0) await sender.SendMessagesAsync(batch, cancellationToken);
        }
        finally
        {
            batch.Dispose();
        }
    }

    private ServiceBusMessage ToServiceBusMessage(GeneratedMessage message)
    {
        var outbound = new ServiceBusMessage(BinaryData.FromString(message.Body))
        {
            MessageId = Guid.NewGuid().ToString("N"),
            ContentType = "application/xml",
        };
        // Service Bus also stamps the broker enqueue time (EnqueuedTime) on every message; this property is the
        // generator's own send time, so latency can be measured from either.
        outbound.ApplicationProperties[SentAtProperty] = _time.GetUtcNow().UtcDateTime.ToString("O");
        outbound.ApplicationProperties[LabelProperty] = message.Label;
        if (message.ExpectedDeadLetterReason is not null)
            outbound.ApplicationProperties[ExpectedReasonProperty] = message.ExpectedDeadLetterReason;
        return outbound;
    }

    public ValueTask DisposeAsync() => sender.DisposeAsync();
}
