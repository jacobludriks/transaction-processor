namespace RunReport;

/// <summary>One saved benchmark run: the configuration it used and everything computed from its data. JSON, camelCase.</summary>
public sealed record RunRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required RunConfiguration Configuration { get; init; }
    public required GeneratorFigures Generator { get; init; }
    public required MeasurementWindow Window { get; init; }
    public required ThroughputFigures Throughput { get; init; }
    public required BacklogFigures Backlog { get; init; }
    public required LatencyFigures Latency { get; init; }
    public required OutcomeFigures Outcomes { get; init; }
    public required DeadLetterFigures DeadLetters { get; init; }
    public required ReconciliationFigures Reconciliation { get; init; }
    public required Verdict Verdict { get; init; }
}

public sealed record GeneratorFigures(long Sent, long Failed, double TargetRate, double AchievedRate, bool HeldTargetRate);

/// <summary>
/// The whole minutes inside the publishing run. The partial minutes at both ends (ramp-up and cool-down) are
/// left out so that they do not drag the sustained rate down.
/// </summary>
public sealed record MeasurementWindow(
    DateTimeOffset StartUtc, DateTimeOffset EndUtc, double DurationSeconds, int ExpectedBins, int BinsWithData, double RunDurationSeconds);

public sealed record ThroughputFigures(
    long OutputsInWindow, double SustainedTps, double MinBinTps, double MaxBinTps, IReadOnlyList<double> PerBinTps);

public sealed record BacklogFigures(
    bool HasData, double? AtWindowStart, double? AtWindowEnd, double? Max,
    double SlopePerSecond, double GrowthOverWindow, double GrowthThreshold, bool Grew, double? AtEndOfData);

/// <summary>
/// Latency percentiles from the histogram buckets, in seconds. <c>*Seconds</c> is an estimate (linear inside
/// the bucket); <c>*UpperBoundSeconds</c> is the upper bound of the bucket that holds the percentile, or null
/// when it falls in the overflow bucket. The verdict uses the upper bound, so it never flatters the pipeline.
/// </summary>
public sealed record LatencyFigures(
    long SampleCount,
    double? P50Seconds, double? P95Seconds, double? P99Seconds,
    double? P50UpperBoundSeconds, double? P95UpperBoundSeconds, double? P99UpperBoundSeconds);

/// <summary>Counts over the whole run (from the first minute of publishing to the end of the data), not just the window.</summary>
public sealed record OutcomeFigures(
    long SentToQueue, long SentToEventHub, long DeadLettered, long Abandoned,
    IReadOnlyDictionary<string, long> DeadLetteredByReason, IReadOnlyDictionary<string, long> AbandonedByReason);

public sealed record DeadLetterFigures(
    long CountedByHost,
    long UnexpectedReasons,
    bool ServerSideDataAvailable,
    long ServerSideGrowth,
    long ServerSideUnaccounted,
    long Unexplained);

public sealed record ReconciliationFigures(
    long GeneratorSent, long SentToQueue, long SentToEventHub, long DeadLettered, long DeadLetteredByServiceBus, long StillInBacklog,
    long Accounted, long Lost, long Duplicates);

public sealed record CriterionResult(string Name, bool Passed, string Detail);

public sealed record Verdict(bool MeetsGoCriteria, IReadOnlyList<CriterionResult> Criteria, IReadOnlyList<string> Warnings);

/// <summary>The go/no-go thresholds from the spec.</summary>
public sealed record GoNoGoCriteria
{
    public double RequiredTps { get; init; } = 300;

    /// <summary>Allowed shortfall against <see cref="RequiredTps"/> (and against the generator's rate), the same 2% the generator allows itself.</summary>
    public double TpsTolerance { get; init; } = 0.02;

    public TimeSpan RequiredWindow { get; init; } = TimeSpan.FromMinutes(10);
    public double MaxP95Seconds { get; init; } = 2.0;

    /// <summary>The backlog counts as growing if the trend adds more than this many seconds of publishing at the target rate.</summary>
    public double BacklogGrowthSecondsOfLoad { get; init; } = 2.0;

    /// <summary>
    /// Whole minutes dropped from each end of the run before measuring. Application Insights exports metrics once a
    /// minute, each instance at its own phase and stamped with the end of the interval, so the minutes next to the
    /// start and the end of the run are smeared across their neighbours. Trimming one minute keeps them out of the
    /// window. The run then needs at least 12 whole minutes, so run the generator for 14.
    /// </summary>
    public int EdgeBinsToTrim { get; init; } = 1;
}
