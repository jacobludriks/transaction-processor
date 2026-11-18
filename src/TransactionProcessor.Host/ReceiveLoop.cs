using Azure.Core;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace TransactionProcessor.Host;

/// <summary>
/// Reads the topic subscription and gives each message body to the handler through <see cref="MessageProcessor"/>.
/// Receive mode is peek-lock with auto-complete off, so a message is completed only after the handler
/// returns (its output has been sent) and a failure leaves it for redelivery.
///
/// Runs <see cref="ProcessorOptions.ProcessorCount"/> processors side by side, each with its own
/// <see cref="ServiceBusClient"/> (its own AMQP connection and receive link), with the instance's concurrency and
/// prefetch split between them. One shared connection was the suspected per-instance ceiling in the peak benchmark.
/// </summary>
public sealed class ReceiveLoop(
    TokenCredential credential,
    TransactionHandler handler,
    IOptions<ProcessorOptions> options,
    HealthState health,
    ProcessorMetrics metrics,
    ILogger<ReceiveLoop> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var messageProcessor = new MessageProcessor(handler.HandleAsync, metrics);
        var concurrency = settings.MaxConcurrentCallsPerProcessor();
        var prefetch = settings.PrefetchCountPerProcessor();

        if (settings.PrefetchCount < settings.MaxConcurrentCalls)
            logger.LogWarning("PrefetchCount {PrefetchCount} is below MaxConcurrentCalls {MaxConcurrentCalls}: handlers will wait on receive round trips",
                settings.PrefetchCount, settings.MaxConcurrentCalls);

        var clients = new List<ServiceBusClient>();
        var processors = new List<ServiceBusProcessor>();
        try
        {
            for (var i = 0; i < settings.ProcessorCount; i++)
            {
                var client = new ServiceBusClient(settings.ServiceBusNamespace, credential);
                clients.Add(client);
                var processor = client.CreateProcessor(settings.TopicName, settings.SubscriptionName, new ServiceBusProcessorOptions
                {
                    ReceiveMode = ServiceBusReceiveMode.PeekLock,
                    AutoCompleteMessages = false,
                    MaxConcurrentCalls = concurrency[i],
                    PrefetchCount = prefetch[i],
                });
                processor.ProcessMessageAsync += args =>
                    messageProcessor.ProcessAsync(args.Message.Body.ToString(), new ServiceBusMessageSettlement(args), args.CancellationToken, args.Message.EnqueuedTime);
                processor.ProcessErrorAsync += args =>
                {
                    logger.LogError(args.Exception, "Service Bus error from {Source} on {EntityPath}", args.ErrorSource, args.EntityPath);
                    return Task.CompletedTask;
                };
                processors.Add(processor);
            }

            await Task.WhenAll(processors.Select(p => p.StartProcessingAsync(stoppingToken)));
            health.Processing = true;
            logger.LogInformation(
                "Receiving from {Topic}/{Subscription} with {ProcessorCount} processors, MaxConcurrentCalls={MaxConcurrentCalls}, PrefetchCount={PrefetchCount} in total",
                settings.TopicName, settings.SubscriptionName, settings.ProcessorCount, settings.MaxConcurrentCalls, settings.PrefetchCount);

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
        finally
        {
            health.Processing = false;
            await Task.WhenAll(processors.Select(p => p.StopProcessingAsync(CancellationToken.None)));
            foreach (var processor in processors) await processor.DisposeAsync();
            foreach (var client in clients) await client.DisposeAsync();
        }
    }
}

/// <summary>Whether the receive loop is running. The health endpoint reports it.</summary>
public sealed class HealthState
{
    public volatile bool Processing;
}
