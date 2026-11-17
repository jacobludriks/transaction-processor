namespace LoadGenerator.Tests;

/// <summary>Rate control, observed through the paced batches and the publisher's summary, on a virtual clock.</summary>
public sealed class PacingTests
{
    private static async Task<List<PacedBatch>> Collect(double rate, TimeSpan duration, IClock clock, int maxBatch = 100)
    {
        var batches = new List<PacedBatch>();
        await foreach (var batch in Pacer.BatchesAsync(rate, duration, clock, TimeSpan.FromMilliseconds(10), maxBatch))
            batches.Add(batch);
        return batches;
    }

    [Theory]
    [InlineData(300, 60)]
    [InlineData(600, 30)]
    [InlineData(7, 100)]
    [InlineData(1, 5)]
    public async Task Exactly_rate_times_duration_messages_are_released(double rate, int seconds)
    {
        var batches = await Collect(rate, TimeSpan.FromSeconds(seconds), new FakeClock());

        Assert.Equal((long)(rate * seconds), batches.Sum(b => (long)b.Count));
    }

    [Fact]
    public async Task The_cumulative_count_tracks_the_target_rate_at_every_point_even_when_wakeups_are_late()
    {
        const double rate = 300;
        // Wake-ups overshoot by 0-9 ms in a repeating pattern.
        var batches = await Collect(rate, TimeSpan.FromSeconds(30), new FakeClock(TimeSpan.FromMilliseconds(1), overshootCycle: 10));

        long released = 0;
        foreach (var batch in batches)
        {
            released += batch.Count;
            // Released exactly what was due by the time of release: within one message of rate x t.
            var scheduled = rate * batch.At.TotalSeconds;
            Assert.InRange(released, scheduled - 1, scheduled + 1);
        }
        Assert.Equal(9_000, released);
    }

    [Fact]
    public async Task Pacing_does_not_drift_over_a_long_run_with_systematically_late_timers()
    {
        // Every wake-up is 5 ms late: a pacing loop that sleeps a fixed interval would run well slow.
        var clock = new FakeClock(TimeSpan.FromMilliseconds(5));
        var batches = await Collect(300, TimeSpan.FromMinutes(10), clock);

        Assert.Equal(180_000, batches.Sum(b => (long)b.Count));
        Assert.InRange(batches[^1].At.TotalSeconds, 600, 600.02);
    }

    [Fact]
    public async Task Batches_are_never_larger_than_the_batch_size()
    {
        var batches = await Collect(50_000, TimeSpan.FromSeconds(2), new FakeClock(TimeSpan.FromMilliseconds(30), 3), maxBatch: 250);

        Assert.All(batches, b => Assert.InRange(b.Count, 1, 250));
        Assert.Equal(100_000, batches.Sum(b => (long)b.Count));
    }

    [Fact]
    public async Task The_publisher_sends_every_message_and_reports_the_rate_it_achieved()
    {
        var clock = new FakeClock(TimeSpan.FromMilliseconds(2), 5);
        var sinks = new List<RecordingSink>();
        var options = new PublishOptions { Rate = 300, Duration = TimeSpan.FromSeconds(20), Senders = 4, BatchSize = 50, Seed = 1 };

        var summary = await new Publisher(clock).RunAsync(options, _ => { var s = new RecordingSink(); lock (sinks) sinks.Add(s); return s; });

        Assert.Equal(6_000, summary.Sent);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(6_000, sinks.Sum(s => s.Messages.Count));
        Assert.InRange(summary.AchievedRate, 297, 303);
        Assert.Equal(300, summary.TargetRate);
    }

    [Fact]
    public async Task The_publisher_uses_every_sender_and_load_messages_carry_the_configured_mix()
    {
        var sinks = new List<RecordingSink>();
        var options = new PublishOptions { Rate = 5_000, Duration = TimeSpan.FromSeconds(20), Senders = 4, BatchSize = 25, Seed = 3, UnmatchedRatio = 0.10 };

        await new Publisher(new FakeClock()).RunAsync(options, _ => { var s = new RecordingSink(); lock (sinks) sinks.Add(s); return s; });

        var all = sinks.SelectMany(s => s.Messages).ToList();
        Assert.Equal(100_000, all.Count);
        Assert.InRange(all.Count(m => m.Label == "load-unmatched-sku") / (double)all.Count, 0.095, 0.105);
        Assert.All(all, m => Assert.Null(m.ExpectedDeadLetterReason));
        Assert.Equal(4, sinks.Count);
    }

    [Fact]
    public async Task A_failing_sender_is_counted_and_reported_without_stopping_the_run()
    {
        var options = new PublishOptions { Rate = 100, Duration = TimeSpan.FromSeconds(10), Senders = 2, BatchSize = 10, Seed = 1 };

        var summary = await new Publisher(new FakeClock()).RunAsync(options, _ => new FailingSink());

        Assert.Equal(0, summary.Sent);
        Assert.Equal(1_000, summary.Failed);
        Assert.Equal("broker unavailable", summary.FirstError);
    }

    [Fact]
    public async Task Progress_is_reported_about_once_per_interval()
    {
        var reports = new List<ProgressReport>();
        var options = new PublishOptions { Rate = 200, Duration = TimeSpan.FromSeconds(30), Senders = 1, BatchSize = 50, Seed = 1, ProgressInterval = TimeSpan.FromSeconds(5) };

        await new Publisher(new FakeClock()).RunAsync(options, _ => new RecordingSink(), reports.Add);

        Assert.InRange(reports.Count, 5, 7);
        Assert.True(reports.Zip(reports.Skip(1), (a, b) => a.Elapsed < b.Elapsed).All(x => x));
    }

    private sealed class RecordingSink : IMessageSink
    {
        public List<GeneratedMessage> Messages { get; } = [];
        public Task SendAsync(IReadOnlyList<GeneratedMessage> messages, CancellationToken cancellationToken = default)
        {
            Messages.AddRange(messages);
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FailingSink : IMessageSink
    {
        public Task SendAsync(IReadOnlyList<GeneratedMessage> messages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("broker unavailable");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
