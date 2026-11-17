using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;

namespace TransactionProcessor.Host;

/// <summary>
/// Pre-aggregated telemetry for the benchmark. At 300+ messages per second, one event per message in
/// Application Insights would be sampled (so counts would be wrong) and would cost money; counters and a
/// histogram are aggregated in the process and exported once a minute, so nothing is sampled away.
///
/// Instruments (meter <see cref="MeterName"/>):
/// <list type="bullet">
/// <item><c>txproc.messages</c>: counter, tags <c>outcome</c> (<c>sent_to_queue</c>, <c>sent_to_event_hub</c>,
/// <c>dead_lettered</c>, <c>abandoned</c>) and, for the last two, <c>reason</c>.</item>
/// <item><c>txproc.message.latency</c>: histogram in seconds, Service Bus enqueue time to the moment the output
/// send completed, tag <c>destination</c> (<c>queue</c> or <c>event_hub</c>).</item>
/// <item><c>txproc.message.latency.bucket</c>: the same latency as a plain counter per bucket, tag <c>le</c>
/// (the bucket's upper bound in seconds, or <c>+Inf</c>) and <c>destination</c>. Application Insights keeps only
/// count, sum, min and max of a histogram, not its buckets, so this counter is what percentiles are computed from.</item>
/// </list>
/// </summary>
public sealed class ProcessorMetrics
{
    public const string MeterName = "TransactionProcessor";

    public const string MessagesInstrument = "txproc.messages";
    public const string LatencyInstrument = "txproc.message.latency";
    public const string LatencyBucketInstrument = "txproc.message.latency.bucket";

    public const string Overflow = "+Inf";

    /// <summary>
    /// Latency bucket upper bounds, in seconds. Dense around the 2 s p95 target (1 to 3 s) so that a p95 close to
    /// the target can be told apart from one that misses it; coarse above that, where only "far too slow" matters.
    /// </summary>
    public static readonly double[] LatencyBucketBoundariesSeconds =
        [0.025, 0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3, 5, 10, 30];

    private static readonly string[] BucketLabels = LatencyBucketBoundariesSeconds.Select(Format).ToArray();

    public Meter Meter { get; } = new(MeterName);

    private readonly Counter<long> _messages;
    private readonly Histogram<double> _latency;
    private readonly Counter<long> _latencyBuckets;

    public ProcessorMetrics()
    {
        _messages = Meter.CreateCounter<long>(MessagesInstrument, "{message}", "Messages handled, by outcome");
        _latency = Meter.CreateHistogram<double>(LatencyInstrument, "s", "Service Bus enqueue time to output send completed");
        _latencyBuckets = Meter.CreateCounter<long>(LatencyBucketInstrument, "{message}", "Latency counted per bucket upper bound (le)");
    }

    public void SentToQueue(double latencySeconds) => Sent("sent_to_queue", "queue", latencySeconds);

    public void SentToEventHub(double latencySeconds) => Sent("sent_to_event_hub", "event_hub", latencySeconds);

    public void SentToQueue() => _messages.Add(1, new TagList { { "outcome", "sent_to_queue" } });

    public void SentToEventHub() => _messages.Add(1, new TagList { { "outcome", "sent_to_event_hub" } });

    public void DeadLettered(string reason) =>
        _messages.Add(1, new TagList { { "outcome", "dead_lettered" }, { "reason", reason } });

    public void Abandoned(string reason) =>
        _messages.Add(1, new TagList { { "outcome", "abandoned" }, { "reason", reason } });

    private void Sent(string outcome, string destination, double latencySeconds)
    {
        _messages.Add(1, new TagList { { "outcome", outcome } });
        var seconds = Math.Max(0, latencySeconds);
        _latency.Record(seconds, new TagList { { "destination", destination } });
        _latencyBuckets.Add(1, new TagList { { "le", BucketLabel(seconds) }, { "destination", destination } });
    }

    private static string BucketLabel(double seconds)
    {
        for (var i = 0; i < LatencyBucketBoundariesSeconds.Length; i++)
            if (seconds <= LatencyBucketBoundariesSeconds[i]) return BucketLabels[i];
        return Overflow;
    }

    private static string Format(double seconds) => seconds.ToString("0.###", CultureInfo.InvariantCulture);
}
