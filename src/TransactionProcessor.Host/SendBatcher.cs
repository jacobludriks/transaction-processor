using System.Threading.Channels;

namespace TransactionProcessor.Host;

/// <summary>
/// Turns many concurrent single-payload sends into fewer batch sends, so one round trip carries many messages.
/// Each caller's <see cref="SendAsync"/> completes only when the batch holding its payload has been sent (and
/// fails if that batch send fails), so a caller that completes its input message after awaiting the send still
/// never completes a message whose output was not sent.
///
/// There is no timer. At most <c>maxConcurrentBatches</c> batch sends are in flight; a payload that arrives while a
/// slot is free leaves at once as a batch of one, so batching adds no latency at low load. Under load, payloads that
/// arrive while every slot is busy wait and leave together (up to <c>maxBatchSize</c>) when a slot frees up.
///
/// If a batch send fails, every caller in it gets the exception, even if the send function had already sent part of
/// it (it may split a group that is too large for one batch). Those inputs are then redelivered and their outputs may
/// be sent twice, which at-least-once delivery already allows.
/// </summary>
public sealed class SendBatcher : IAsyncDisposable
{
    private readonly Channel<Pending> _pending = Channel.CreateUnbounded<Pending>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Func<IReadOnlyList<string>, CancellationToken, Task> _sendBatch;
    private readonly Action<int>? _batchSent;
    private readonly int _maxBatchSize;
    private readonly int _maxConcurrentBatches;
    private readonly SemaphoreSlim _slots;
    private readonly Task _loop;

    /// <param name="sendBatch">Sends one group of payloads. It may split the group into several service batches if it does not fit in one.</param>
    /// <param name="maxBatchSize">Most payloads handed to <paramref name="sendBatch"/> at once.</param>
    /// <param name="maxConcurrentBatches">Most calls to <paramref name="sendBatch"/> in flight at once.</param>
    /// <param name="batchSent">Called with the group size after each successful batch send (telemetry).</param>
    public SendBatcher(
        Func<IReadOnlyList<string>, CancellationToken, Task> sendBatch,
        int maxBatchSize = 100,
        int maxConcurrentBatches = 4,
        Action<int>? batchSent = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxConcurrentBatches, 1);
        _sendBatch = sendBatch;
        _batchSent = batchSent;
        _maxBatchSize = maxBatchSize;
        _maxConcurrentBatches = maxConcurrentBatches;
        _slots = new SemaphoreSlim(maxConcurrentBatches, maxConcurrentBatches);
        _loop = Task.Run(RunAsync);
    }

    /// <summary>Queues the payload and completes when the batch holding it has been sent.</summary>
    public Task SendAsync(string payload, CancellationToken cancellationToken = default)
    {
        var pending = new Pending(payload);
        if (!_pending.Writer.TryWrite(pending))
            throw new ObjectDisposedException(nameof(SendBatcher));
        // Cancelling stops the caller waiting; the payload may still be sent.
        return pending.Completion.Task.WaitAsync(cancellationToken);
    }

    private async Task RunAsync()
    {
        var reader = _pending.Reader;
        while (await reader.WaitToReadAsync())
        {
            // Wait for a free slot first: payloads that arrive meanwhile join this batch.
            await _slots.WaitAsync();
            var batch = new List<Pending>();
            while (batch.Count < _maxBatchSize && reader.TryRead(out var pending))
                batch.Add(pending);
            if (batch.Count == 0)
            {
                _slots.Release();
                continue;
            }
            _ = SendAndReleaseAsync(batch);
        }

        // Writer completed and everything read: wait for the batches still in flight.
        for (var i = 0; i < _maxConcurrentBatches; i++)
            await _slots.WaitAsync();
    }

    private async Task SendAndReleaseAsync(List<Pending> batch)
    {
        try
        {
            var payloads = new string[batch.Count];
            for (var i = 0; i < batch.Count; i++) payloads[i] = batch[i].Payload;
            await _sendBatch(payloads, CancellationToken.None);
            foreach (var pending in batch) pending.Completion.TrySetResult();
            try { _batchSent?.Invoke(batch.Count); } catch { /* telemetry must not affect sends */ }
        }
        catch (Exception ex)
        {
            foreach (var pending in batch) pending.Completion.TrySetException(ex);
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>Stops accepting payloads, sends the ones already queued and waits for every batch in flight.</summary>
    public async ValueTask DisposeAsync()
    {
        _pending.Writer.TryComplete();
        await _loop;
        _slots.Dispose();
    }

    private sealed class Pending(string payload)
    {
        public string Payload { get; } = payload;

        // Asynchronous continuations: completing a batch must not run every caller's continuation on the send path.
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
