using System.Diagnostics.Metrics;
using Microsoft.Extensions.Time.Testing;
using TransactionProcessor.Host;
using TransactionProcessor.Tests.Infrastructure;

namespace TransactionProcessor.Tests;

/// <summary>
/// The pre-aggregated telemetry the host emits for each message, observed the way an exporter sees it
/// (through a meter listener): counts by outcome and a latency distribution from Service Bus enqueue time
/// to the moment the output send completed.
/// </summary>
public class MessageMetricsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private sealed record Measurement(string Instrument, double Value, Dictionary<string, string?> Tags);

    private sealed class Recorder : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<Measurement> Measurements { get; } = [];

        public Recorder(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i, v, tags));
            _listener.Start();
        }

        private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var dict = new Dictionary<string, string?>();
            foreach (var tag in tags) dict[tag.Key] = tag.Value?.ToString();
            lock (Measurements) Measurements.Add(new Measurement(instrument.Name, value, dict));
        }

        public IReadOnlyList<Measurement> Of(string instrument)
        {
            lock (Measurements) return Measurements.Where(m => m.Instrument == instrument).ToList();
        }

        public void Dispose() => _listener.Dispose();
    }

    private static async Task<Recorder> ProcessAsync(
        HandlerOutcome? outcome, TimeSpan? sinceEnqueued = null, Func<string, CancellationToken, Task<HandlerOutcome>>? handle = null,
        bool swallowException = false)
    {
        var metrics = new ProcessorMetrics();
        var recorder = new Recorder(metrics.Meter);
        var time = new FakeTimeProvider(Now);
        var processor = new MessageProcessor(handle ?? ((_, _) => Task.FromResult(outcome!)), metrics, time);
        try
        {
            await processor.ProcessAsync("<xml/>", new FakeSettlement(), default, Now - (sinceEnqueued ?? TimeSpan.Zero));
        }
        catch when (swallowException) { }
        return recorder;
    }

    [Fact]
    public async Task A_message_sent_to_the_queue_is_counted_as_sent_to_queue()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.SendToQueue());

        var count = Assert.Single(recorder.Of("txproc.messages"));
        Assert.Equal(1, count.Value);
        Assert.Equal("sent_to_queue", count.Tags["outcome"]);
    }

    [Fact]
    public async Task A_message_sent_to_the_event_hub_is_counted_as_sent_to_event_hub()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.SendToEventHub());

        Assert.Equal("sent_to_event_hub", Assert.Single(recorder.Of("txproc.messages")).Tags["outcome"]);
    }

    [Fact]
    public async Task A_dead_lettered_message_is_counted_with_its_reason()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.DeadLetter(DeadLetterReasons.ItemNotFound));

        var count = Assert.Single(recorder.Of("txproc.messages"));
        Assert.Equal("dead_lettered", count.Tags["outcome"]);
        Assert.Equal("ItemNotFound", count.Tags["reason"]);
    }

    [Fact]
    public async Task A_retry_is_counted_as_abandoned_for_a_transient_failure()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.Retry());

        var count = Assert.Single(recorder.Of("txproc.messages"));
        Assert.Equal("abandoned", count.Tags["outcome"]);
        Assert.Equal("transient_failure", count.Tags["reason"]);
    }

    [Fact]
    public async Task An_unexpected_handler_exception_is_counted_as_abandoned_for_an_unhandled_exception()
    {
        using var recorder = await ProcessAsync(null, handle: (_, _) => throw new InvalidOperationException("boom"), swallowException: true);

        var count = Assert.Single(recorder.Of("txproc.messages"));
        Assert.Equal("abandoned", count.Tags["outcome"]);
        Assert.Equal("unhandled_exception", count.Tags["reason"]);
    }

    [Theory]
    [InlineData(typeof(HandlerOutcome.SendToQueue), "queue")]
    [InlineData(typeof(HandlerOutcome.SendToEventHub), "event_hub")]
    public async Task Latency_runs_from_enqueue_to_output_sent_and_covers_both_destinations(Type outcomeType, string destination)
    {
        var outcome = (HandlerOutcome)Activator.CreateInstance(outcomeType)!;
        using var recorder = await ProcessAsync(outcome, sinceEnqueued: TimeSpan.FromMilliseconds(1250));

        var latency = Assert.Single(recorder.Of("txproc.message.latency"));
        Assert.Equal(1.25, latency.Value, precision: 6);
        Assert.Equal(destination, latency.Tags["destination"]);
    }

    [Fact]
    public async Task Latency_is_measured_when_the_handler_returns_not_after_the_message_is_settled()
    {
        // The handler "takes" 300 ms of virtual time to send its output; settlement then takes another 5 s.
        var metrics = new ProcessorMetrics();
        using var recorder = new Recorder(metrics.Meter);
        var time = new FakeTimeProvider(Now);
        var processor = new MessageProcessor(
            (_, _) => { time.Advance(TimeSpan.FromMilliseconds(300)); return Task.FromResult<HandlerOutcome>(new HandlerOutcome.SendToQueue()); },
            metrics, time);
        var settlement = new SlowSettlement(time, TimeSpan.FromSeconds(5));

        await processor.ProcessAsync("<xml/>", settlement, default, Now - TimeSpan.FromMilliseconds(200));

        Assert.Equal(0.5, Assert.Single(recorder.Of("txproc.message.latency")).Value, precision: 6);
    }

    [Theory]
    [InlineData(0.01, "0.025")]
    [InlineData(0.025, "0.025")]
    [InlineData(0.9, "1")]
    [InlineData(2.0, "2")]
    [InlineData(2.01, "2.5")]
    [InlineData(500, "+Inf")]
    public async Task Each_latency_is_also_counted_in_the_bucket_with_the_smallest_upper_bound_that_holds_it(double seconds, string le)
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.SendToQueue(), TimeSpan.FromSeconds(seconds));

        var bucket = Assert.Single(recorder.Of("txproc.message.latency.bucket"));
        Assert.Equal(1, bucket.Value);
        Assert.Equal(le, bucket.Tags["le"]);
    }

    [Fact]
    public async Task Messages_that_are_not_sent_have_no_latency()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.DeadLetter(DeadLetterReasons.InvalidXml), TimeSpan.FromSeconds(1));

        Assert.Empty(recorder.Of("txproc.message.latency"));
        Assert.Empty(recorder.Of("txproc.message.latency.bucket"));
    }

    [Fact]
    public async Task A_clock_skew_that_puts_the_enqueue_time_in_the_future_records_zero_latency_not_a_negative_one()
    {
        using var recorder = await ProcessAsync(new HandlerOutcome.SendToQueue(), TimeSpan.FromSeconds(-3));

        Assert.Equal(0, Assert.Single(recorder.Of("txproc.message.latency")).Value);
    }

    [Fact]
    public async Task Without_an_enqueue_time_the_message_is_counted_but_no_latency_is_recorded()
    {
        var metrics = new ProcessorMetrics();
        using var recorder = new Recorder(metrics.Meter);
        await new MessageProcessor((_, _) => Task.FromResult<HandlerOutcome>(new HandlerOutcome.SendToQueue()), metrics)
            .ProcessAsync("<xml/>", new FakeSettlement());

        Assert.Single(recorder.Of("txproc.messages"));
        Assert.Empty(recorder.Of("txproc.message.latency"));
    }

    private sealed class SlowSettlement(FakeTimeProvider time, TimeSpan delay) : IMessageSettlement
    {
        public Task CompleteAsync(CancellationToken cancellationToken = default) { time.Advance(delay); return Task.CompletedTask; }
        public Task DeadLetterAsync(string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task AbandonAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
