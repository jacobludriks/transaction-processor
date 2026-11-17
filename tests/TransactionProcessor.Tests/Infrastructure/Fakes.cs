namespace TransactionProcessor.Tests.Infrastructure;

/// <summary>In-memory queue sender that records what was sent.</summary>
public sealed class FakeQueueSender : IQueueSender
{
    private readonly List<string> _sent = [];
    public IReadOnlyList<string> Sent => _sent;

    public Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        _sent.Add(json);
        return Task.CompletedTask;
    }
}

/// <summary>In-memory event hub sender that records what was sent.</summary>
public sealed class FakeEventHubSender : IEventHubSender
{
    private readonly List<string> _sent = [];
    public IReadOnlyList<string> Sent => _sent;

    public Task SendAsync(string json, CancellationToken cancellationToken = default)
    {
        _sent.Add(json);
        return Task.CompletedTask;
    }
}

/// <summary>Queue sender whose sends always fail, as when Service Bus is unavailable.</summary>
public sealed class FailingQueueSender : IQueueSender
{
    public Task SendAsync(string json, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The queue is unavailable.");
}

/// <summary>Event hub sender whose sends always fail, as when Event Hubs is unavailable.</summary>
public sealed class FailingEventHubSender : IEventHubSender
{
    public Task SendAsync(string json, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("The event hub is unavailable.");
}
