namespace RunReport.Tests;

/// <summary>
/// Builds the inputs of a run whose numbers are known by construction: messages are published at a constant
/// rate for a fixed duration, and processed at a constant rate with no lag, so every minute that lies wholly inside
/// the run holds exactly rate * 60 messages. The latency distribution is a known one over known buckets.
/// This validates the arithmetic of the report; it says nothing about what a real Azure run will produce.
/// </summary>
public sealed class SyntheticRun
{
    // Not on a minute boundary on purpose: the partial minutes at both ends must not drag the rate down.
    public DateTimeOffset Start { get; init; } = new(2026, 11, 17, 10, 0, 20, TimeSpan.Zero);
    public TimeSpan Duration { get; init; } = TimeSpan.FromMinutes(14);

    public double PublishTps { get; init; } = 300;
    public double ProcessTps { get; init; } = 300;
    public double UnmatchedShare { get; init; } = 0.10;

    /// <summary>Per-bucket share of messages. Bounds 0.1, 0.25, 0.5, 1, 2, 5, +Inf. Cumulative: 10%, 50%, 80%, 95%, 99%, 100%, 100%.</summary>
    public double[] LatencyShares { get; init; } = [0.10, 0.40, 0.30, 0.15, 0.04, 0.01, 0.0];

    public static readonly string[] Bounds = ["0.1", "0.25", "0.5", "1", "2", "5", "+Inf"];

    /// <summary>Backlog (active messages) at the given time. Default: a steady 120 while publishing, then drained to 0.</summary>
    public Func<DateTimeOffset, double>? Backlog { get; init; }

    /// <summary>Messages in the dead-letter queue at the given time. Default: always empty.</summary>
    public Func<DateTimeOffset, double> DeadLetterQueue { get; init; } = _ => 0;

    /// <summary>Extra outcome rows, for example dead-lettered messages or abandons.</summary>
    public List<OutcomeSample> ExtraOutcomes { get; } = [];

    /// <summary>Messages the processor produced fewer outputs for than the generator sent (a lost-message case).</summary>
    public long OutputsMissing { get; init; }

    public DateTimeOffset End => Start + Duration;

    public RunConfiguration Configuration(Func<RunConfiguration, RunConfiguration>? adjust = null)
    {
        var config = new RunConfiguration
        {
            Name = "synthetic", InstanceCount = 3, MaxConcurrentCalls = 16, DatabaseTier = "GP_Gen5_4", TargetPublishRate = PublishTps,
        };
        return adjust?.Invoke(config) ?? config;
    }

    public GeneratorSummary Generator(bool held = true) => new()
    {
        StartedAtUtc = Start, EndedAtUtc = End, TargetRate = PublishTps,
        Sent = (long)(PublishTps * Duration.TotalSeconds), Failed = 0,
        AchievedRate = PublishTps, HeldTargetRate = held,
    };

    public MetricsExport Export()
    {
        var outcomes = new List<OutcomeSample>();
        var buckets = new List<LatencyBucketSample>();
        var firstBin = FloorToMinute(Start);

        var backlog = new List<BacklogSample>();
        var dlq = new List<DeadLetterQueueSample>();
        var first = true;
        for (var bin = firstBin.AddMinutes(-3); bin <= End.AddMinutes(4); bin = bin.AddMinutes(1))
        {
            backlog.Add(new BacklogSample(bin, (Backlog ?? (t => t < End ? 120 : 0))(bin)));
            dlq.Add(new DeadLetterQueueSample(bin, DeadLetterQueue(bin)));
        }

        for (var bin = firstBin; bin < End; bin = bin.AddMinutes(1))
        {
            var overlap = Overlap(bin, bin.AddMinutes(1));
            var processed = (long)Math.Round(ProcessTps * overlap);
            if (first && OutputsMissing != 0) { processed -= OutputsMissing; first = false; }
            var matched = processed - (long)Math.Round(processed * UnmatchedShare);
            outcomes.Add(new OutcomeSample(bin, "sent_to_queue", null, matched));
            outcomes.Add(new OutcomeSample(bin, "sent_to_event_hub", null, processed - matched));
            for (var i = 0; i < Bounds.Length; i++)
                buckets.Add(new LatencyBucketSample(bin, Bounds[i], (long)Math.Round(processed * LatencyShares[i])));
        }

        outcomes.AddRange(ExtraOutcomes);
        return new MetricsExport { BinSeconds = 60, Outcomes = outcomes, LatencyBuckets = buckets, Backlog = backlog, DeadLetterQueue = dlq };
    }

    private double Overlap(DateTimeOffset binStart, DateTimeOffset binEnd)
    {
        var from = binStart > Start ? binStart : Start;
        var to = binEnd < End ? binEnd : End;
        return Math.Max(0, (to - from).TotalSeconds);
    }

    private static DateTimeOffset FloorToMinute(DateTimeOffset t) => new(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0, t.Offset);
}
