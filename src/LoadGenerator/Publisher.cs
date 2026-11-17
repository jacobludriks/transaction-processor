using System.Threading.Channels;

namespace LoadGenerator;

/// <summary>Where generated messages go. A real implementation publishes to Service Bus; the dry-run one discards them.</summary>
public interface IMessageSink : IAsyncDisposable
{
    Task SendAsync(IReadOnlyList<GeneratedMessage> messages, CancellationToken cancellationToken = default);
}

/// <summary>Dry-run sink: does the per-message work a real sender would do on the body (encoding it) and discards it.</summary>
public sealed class NoOpSink : IMessageSink
{
    private long _bytes;

    public long Bytes => Interlocked.Read(ref _bytes);

    public Task SendAsync(IReadOnlyList<GeneratedMessage> messages, CancellationToken cancellationToken = default)
    {
        long bytes = 0;
        foreach (var message in messages)
            bytes += System.Text.Encoding.UTF8.GetByteCount(message.Body);
        Interlocked.Add(ref _bytes, bytes);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed class PublishOptions
{
    /// <summary>Target messages per second.</summary>
    public double Rate { get; init; } = 300;

    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>Concurrent senders. Each has its own generator (seeded Seed + index) and its own sink.</summary>
    public int Senders { get; init; } = 4;

    /// <summary>Largest number of messages handed to a sender in one go (and so, at most, one Service Bus batch).</summary>
    public int BatchSize { get; init; } = 100;

    public double UnmatchedRatio { get; init; } = MessageGenerator.DefaultUnmatchedRatio;

    public int Seed { get; init; } = 12345;

    public TimeSpan ProgressInterval { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How often the pacing loop wakes. Late wake-ups are made up, so this only bounds burst size and latency.</summary>
    public TimeSpan Tick { get; init; } = TimeSpan.FromMilliseconds(10);
}

public sealed record ProgressReport(TimeSpan Elapsed, long Released, long Sent, long Failed, double IntervalRate, double AverageRate)
{
    /// <summary>Released by the pacer but not yet sent or failed: messages queued for a sender.</summary>
    public long Backlog => Released - Sent - Failed;
}

public sealed record RunSummary(double TargetRate, TimeSpan Elapsed, long Sent, long Failed, string? FirstError)
{
    public double AchievedRate => Elapsed > TimeSpan.Zero ? Sent / Elapsed.TotalSeconds : 0;

    /// <summary>Whether the generator achieved at least 98% of the target rate with no send failures.</summary>
    public bool HeldTargetRate => Failed == 0 && AchievedRate / TargetRate >= 0.98;
}

/// <summary>
/// Publishes load messages at a target rate: one pacing loop releases batches on an absolute schedule into a
/// bounded channel, and several senders generate and send them concurrently. If the senders cannot keep up the
/// channel fills, the pacer stalls and the achieved rate drops below the target (and the summary says so).
/// </summary>
public sealed class Publisher(IClock clock)
{
    public async Task<RunSummary> RunAsync(
        PublishOptions options,
        Func<int, IMessageSink> createSink,
        Action<ProgressReport>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(Math.Max(64, options.Senders * 16))
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        long sent = 0, failed = 0;
        string? firstError = null;

        var workers = Enumerable.Range(0, options.Senders).Select(index => Task.Run(async () =>
        {
            var generator = new MessageGenerator(options.Seed + index, options.UnmatchedRatio);
            await using var sink = createSink(index);
            await foreach (var count in channel.Reader.ReadAllAsync(cancellationToken))
            {
                var batch = new List<GeneratedMessage>(count);
                for (var i = 0; i < count; i++) batch.Add(generator.NextLoadMessage());
                try
                {
                    await sink.SendAsync(batch, cancellationToken);
                    Interlocked.Add(ref sent, count);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Interlocked.Add(ref failed, count);
                    Interlocked.CompareExchange(ref firstError, ex.Message, null);
                }
            }
        }, cancellationToken)).ToArray();

        var start = clock.Elapsed;
        long released = 0, lastReportSent = 0;
        var lastReportAt = TimeSpan.Zero;
        try
        {
            await foreach (var batch in Pacer.BatchesAsync(options.Rate, options.Duration, clock, options.Tick, options.BatchSize, cancellationToken))
            {
                await channel.Writer.WriteAsync(batch.Count, cancellationToken);
                released += batch.Count;

                var elapsed = clock.Elapsed - start;
                if (onProgress is not null && elapsed - lastReportAt >= options.ProgressInterval)
                {
                    var sentNow = Interlocked.Read(ref sent);
                    onProgress(new ProgressReport(
                        elapsed, released, sentNow, Interlocked.Read(ref failed),
                        (sentNow - lastReportSent) / (elapsed - lastReportAt).TotalSeconds,
                        sentNow / elapsed.TotalSeconds));
                    lastReportAt = elapsed;
                    lastReportSent = sentNow;
                }
            }
        }
        finally
        {
            channel.Writer.Complete();
        }
        await Task.WhenAll(workers);

        return new RunSummary(options.Rate, clock.Elapsed - start, Interlocked.Read(ref sent), Interlocked.Read(ref failed), firstError);
    }
}
