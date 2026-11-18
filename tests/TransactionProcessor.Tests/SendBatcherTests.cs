using TransactionProcessor.Host;

namespace TransactionProcessor.Tests;

/// <summary>How the output batcher combines concurrent sends, and that a send completes only once its batch is sent.</summary>
public class SendBatcherTests
{
    /// <summary>A batch send function whose sends block until released, recording each group it was given.</summary>
    private sealed class GatedSink
    {
        private readonly object _lock = new();
        private TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyList<string>> Batches { get; } = [];
        public SemaphoreSlim Started { get; } = new(0);
        public Exception? FailWith { get; set; }

        public async Task SendAsync(IReadOnlyList<string> payloads, CancellationToken _)
        {
            Task gate;
            lock (_lock) { Batches.Add(payloads); gate = _gate.Task; }
            Started.Release();
            await gate;
            if (FailWith is { } ex) throw ex;
        }

        public void ReleaseAll()
        {
            lock (_lock) { _gate.TrySetResult(); _gate = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        }

        public void Open() { lock (_lock) _gate.TrySetResult(); }
    }

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_single_send_goes_out_at_once_as_a_batch_of_one()
    {
        var sink = new GatedSink();
        sink.Open();
        await using var batcher = new SendBatcher(sink.SendAsync);

        await batcher.SendAsync("a").WaitAsync(Timeout);

        Assert.Equal([["a"]], sink.Batches);
    }

    [Fact]
    public async Task Sends_arriving_while_every_slot_is_busy_leave_together_in_one_batch()
    {
        var sink = new GatedSink();
        await using var batcher = new SendBatcher(sink.SendAsync, maxConcurrentBatches: 1);

        var first = batcher.SendAsync("first");
        await sink.Started.WaitAsync(Timeout); // the only slot is now busy
        var waiting = new[] { batcher.SendAsync("b"), batcher.SendAsync("c"), batcher.SendAsync("d") };

        sink.Open();
        await Task.WhenAll([first, .. waiting]).WaitAsync(Timeout);

        Assert.Equal(2, sink.Batches.Count);
        Assert.Equal(["first"], sink.Batches[0]);
        Assert.Equal(["b", "c", "d"], sink.Batches[1]);
    }

    [Fact]
    public async Task A_batch_never_holds_more_than_the_maximum_batch_size()
    {
        var sink = new GatedSink();
        await using var batcher = new SendBatcher(sink.SendAsync, maxBatchSize: 2, maxConcurrentBatches: 1);

        var first = batcher.SendAsync("first");
        await sink.Started.WaitAsync(Timeout);
        var waiting = Enumerable.Range(1, 5).Select(i => batcher.SendAsync($"m{i}")).ToArray();

        sink.Open();
        await Task.WhenAll([first, .. waiting]).WaitAsync(Timeout);

        Assert.All(sink.Batches, b => Assert.InRange(b.Count, 1, 2));
        Assert.Equal(6, sink.Batches.Sum(b => b.Count));
    }

    [Fact]
    public async Task A_send_does_not_complete_until_its_batch_has_been_sent()
    {
        var sink = new GatedSink();
        await using var batcher = new SendBatcher(sink.SendAsync);

        var send = batcher.SendAsync("a");
        await sink.Started.WaitAsync(Timeout);
        await Task.Delay(50);
        Assert.False(send.IsCompleted);

        sink.ReleaseAll();
        await send.WaitAsync(Timeout);
    }

    [Fact]
    public async Task A_failed_batch_send_fails_every_send_in_that_batch()
    {
        var sink = new GatedSink { FailWith = new InvalidOperationException("queue unavailable") };
        await using var batcher = new SendBatcher(sink.SendAsync, maxConcurrentBatches: 1);

        var first = batcher.SendAsync("first");
        await sink.Started.WaitAsync(Timeout);
        var b = batcher.SendAsync("b");
        var c = batcher.SendAsync("c");
        sink.Open();

        await Assert.ThrowsAsync<InvalidOperationException>(() => first.WaitAsync(Timeout));
        await Assert.ThrowsAsync<InvalidOperationException>(() => b.WaitAsync(Timeout));
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.WaitAsync(Timeout));
    }

    [Fact]
    public async Task Successful_batches_are_reported_with_their_size()
    {
        var sink = new GatedSink();
        var sizes = new List<int>();
        await using var batcher = new SendBatcher(sink.SendAsync, maxConcurrentBatches: 1, batchSent: n => { lock (sizes) sizes.Add(n); });

        var first = batcher.SendAsync("first");
        await sink.Started.WaitAsync(Timeout);
        var rest = new[] { batcher.SendAsync("b"), batcher.SendAsync("c") };
        sink.Open();
        await Task.WhenAll([first, .. rest]).WaitAsync(Timeout);

        Assert.Equal([1, 2], sizes);
    }

    [Fact]
    public async Task Disposing_sends_what_is_already_queued_then_refuses_new_sends()
    {
        var sink = new GatedSink();
        var batcher = new SendBatcher(sink.SendAsync, maxConcurrentBatches: 1);

        var first = batcher.SendAsync("first");
        await sink.Started.WaitAsync(Timeout);
        var queued = batcher.SendAsync("queued");

        var dispose = batcher.DisposeAsync().AsTask();
        sink.Open();
        await dispose.WaitAsync(Timeout);

        await Task.WhenAll(first, queued).WaitAsync(Timeout);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => batcher.SendAsync("late"));
    }
}

public class ProcessorOptionsTests
{
    [Theory]
    [InlineData(16, 4, new[] { 4, 4, 4, 4 })]
    [InlineData(48, 5, new[] { 10, 10, 10, 9, 9 })]
    [InlineData(150, 4, new[] { 38, 38, 37, 37 })]
    [InlineData(0, 3, new[] { 0, 0, 0 })]
    [InlineData(7, 1, new[] { 7 })]
    public void Split_divides_a_total_as_evenly_as_possible(int total, int parts, int[] expected)
        => Assert.Equal(expected, ProcessorOptions.Split(total, parts));

    [Fact]
    public void More_processors_than_concurrent_calls_is_invalid()
    {
        var options = new ProcessorOptions { MaxConcurrentCalls = 2, ProcessorCount = 3 };
        Assert.NotEmpty(options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)));
    }

    [Fact]
    public void The_defaults_are_valid_and_prefetch_covers_concurrency()
    {
        var options = new ProcessorOptions();
        Assert.Empty(options.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(options)));
        Assert.True(options.PrefetchCount >= options.MaxConcurrentCalls);
    }
}
