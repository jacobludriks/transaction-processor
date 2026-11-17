using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;

namespace TransactionProcessor.Host;

/// <summary>
/// Reads the topic subscription and gives each message body to the handler through <see cref="MessageProcessor"/>.
/// Receive mode is peek-lock with auto-complete off, so a message is completed only after the handler
/// returns (its output has been sent) and a failure leaves it for redelivery.
/// </summary>
public sealed class ReceiveLoop(
    ServiceBusClient client,
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

        await using var processor = client.CreateProcessor(settings.TopicName, settings.SubscriptionName, new ServiceBusProcessorOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            AutoCompleteMessages = false,
            MaxConcurrentCalls = settings.MaxConcurrentCalls,
            PrefetchCount = settings.PrefetchCount,
        });

        processor.ProcessMessageAsync += args =>
            messageProcessor.ProcessAsync(args.Message.Body.ToString(), new ServiceBusMessageSettlement(args), args.CancellationToken, args.Message.EnqueuedTime);
        processor.ProcessErrorAsync += args =>
        {
            logger.LogError(args.Exception, "Service Bus error from {Source} on {EntityPath}", args.ErrorSource, args.EntityPath);
            return Task.CompletedTask;
        };

        await processor.StartProcessingAsync(stoppingToken);
        health.Processing = true;
        logger.LogInformation("Receiving from {Topic}/{Subscription} with MaxConcurrentCalls={MaxConcurrentCalls}, PrefetchCount={PrefetchCount}",
            settings.TopicName, settings.SubscriptionName, settings.MaxConcurrentCalls, settings.PrefetchCount);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            health.Processing = false;
            await processor.StopProcessingAsync(CancellationToken.None);
        }
    }
}

/// <summary>Whether the receive loop is running. The health endpoint reports it.</summary>
public sealed class HealthState
{
    public volatile bool Processing;
}
