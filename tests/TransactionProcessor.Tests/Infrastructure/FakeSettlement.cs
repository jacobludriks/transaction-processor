using TransactionProcessor.Host;

namespace TransactionProcessor.Tests.Infrastructure;

/// <summary>Records what the receive loop did to a message, with no Service Bus involved.</summary>
public sealed class FakeSettlement : IMessageSettlement
{
    public List<string> Actions { get; } = [];

    public Task CompleteAsync(CancellationToken cancellationToken = default)
    {
        Actions.Add("complete");
        return Task.CompletedTask;
    }

    public Task DeadLetterAsync(string reason, CancellationToken cancellationToken = default)
    {
        Actions.Add($"dead-letter:{reason}");
        return Task.CompletedTask;
    }

    public Task AbandonAsync(CancellationToken cancellationToken = default)
    {
        Actions.Add("abandon");
        return Task.CompletedTask;
    }
}
