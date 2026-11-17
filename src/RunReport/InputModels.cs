namespace RunReport;

/// <summary>
/// What was set up for the run. Saved with the result so runs can be compared. JSON file, camelCase names.
/// </summary>
public sealed record RunConfiguration
{
    /// <summary>Optional label, for example "baseline" or "db-8-vcores".</summary>
    public string? Name { get; init; }

    public int InstanceCount { get; init; }
    public int MaxConcurrentCalls { get; init; }
    public int? PrefetchCount { get; init; }

    /// <summary>Database tier as deployed, for example <c>GP_Gen5_4</c> (General Purpose, 4 vCores).</summary>
    public string DatabaseTier { get; init; } = "";

    /// <summary>Target publish rate in messages per second.</summary>
    public double TargetPublishRate { get; init; }

    /// <summary>
    /// When the generator started and stopped publishing. Optional: when absent the generator's run summary
    /// supplies them.
    /// </summary>
    public DateTimeOffset? StartUtc { get; init; }

    public DateTimeOffset? EndUtc { get; init; }

    /// <summary>
    /// Dead-letter reasons that are expected in this run (for example when the functional set was sent on
    /// purpose). Anything else counts as an unexplained dead-letter. Empty for a normal load run.
    /// </summary>
    public IReadOnlyList<string> ExpectedDeadLetterReasons { get; init; } = [];

    public string? Notes { get; init; }
}

/// <summary>Count of messages with one outcome (and reason, for dead-lettered and abandoned) in one time bin.</summary>
public sealed record OutcomeSample(DateTimeOffset Timestamp, string Outcome, string? Reason, long Count);

/// <summary>Messages whose latency fell in the bucket with upper bound <c>Le</c> seconds (<c>+Inf</c> for the overflow bucket). Per bin, not cumulative.</summary>
public sealed record LatencyBucketSample(DateTimeOffset Timestamp, string Le, long Count);

/// <summary>Active messages in the input subscription (Azure Monitor metric ActiveMessages).</summary>
public sealed record BacklogSample(DateTimeOffset Timestamp, double ActiveMessages);

/// <summary>Messages in the subscription's dead-letter queue (Azure Monitor metric DeadletteredMessages).</summary>
public sealed record DeadLetterQueueSample(DateTimeOffset Timestamp, double Messages);

/// <summary>
/// The time series exported from Application Insights (outcomes, latency buckets) and Azure Monitor (backlog,
/// dead-letter queue). JSON file, camelCase names; see the README for the queries that produce it.
/// </summary>
public sealed record MetricsExport
{
    /// <summary>Width of the time bins of the outcome and latency series, in seconds (Application Insights exports once a minute).</summary>
    public int BinSeconds { get; init; } = 60;

    public IReadOnlyList<OutcomeSample> Outcomes { get; init; } = [];
    public IReadOnlyList<LatencyBucketSample> LatencyBuckets { get; init; } = [];
    public IReadOnlyList<BacklogSample> Backlog { get; init; } = [];
    public IReadOnlyList<DeadLetterQueueSample> DeadLetterQueue { get; init; } = [];
}

/// <summary>The generator's own account of the run, from its <c>--summary-file</c>.</summary>
public sealed record GeneratorSummary
{
    public DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset EndedAtUtc { get; init; }
    public double TargetRate { get; init; }
    public long Sent { get; init; }
    public long Failed { get; init; }
    public double AchievedRate { get; init; }
    public bool HeldTargetRate { get; init; }
}
