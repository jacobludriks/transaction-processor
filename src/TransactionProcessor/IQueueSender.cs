namespace TransactionProcessor;

/// <summary>Sends a JSON payload to the output Service Bus queue (recognised SKU).</summary>
public interface IQueueSender
{
    Task SendAsync(string json, CancellationToken cancellationToken = default);
}
