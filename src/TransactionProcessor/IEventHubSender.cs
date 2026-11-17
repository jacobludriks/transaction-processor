namespace TransactionProcessor;

/// <summary>Sends a JSON payload to the output event hub (unrecognised SKU).</summary>
public interface IEventHubSender
{
    Task SendAsync(string json, CancellationToken cancellationToken = default);
}
