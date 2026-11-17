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
