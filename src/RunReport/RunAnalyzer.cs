using System.Globalization;
using System.Text.RegularExpressions;
using TransactionProcessor;

using static RunReport.InvariantText;

namespace RunReport;

/// <summary>
/// Turns one run's configuration, exported time series and generator summary into a <see cref="RunRecord"/>:
/// sustained TPS, latency percentiles, backlog trend, outcome and dead-letter counts, a reconciliation against
/// the generator's own count, and a verdict against the go/no-go criteria.
/// </summary>
public static class RunAnalyzer
{
    public const string ThroughputCriterion = "Sustained throughput";
    public const string BacklogCriterion = "Backlog not growing";
    public const string LatencyCriterion = "p95 latency under 2 s";
    public const string LostCriterion = "Zero lost messages";
    public const string DeadLetterCriterion = "No unexplained dead-letters";
    public const string GeneratorCriterion = "Generator held the target rate";


    private static CriterionResult Result(string name, bool passed, FormattableString detail) => new(name, passed, Inv(detail));

    public static bool MeetsAll(IEnumerable<CriterionResult> criteria) => criteria.All(c => c.Passed);

    private static readonly GoNoGoCriteria Criteria = new();

    public static RunRecord Analyze(
        RunConfiguration configuration, MetricsExport export, GeneratorSummary generator, DateTimeOffset recordedAtUtc)
    {
        var warnings = new List<string>();

        var window = ComputeWindow(configuration, generator, export.BinSeconds);
        var throughput = MeasureThroughput(export, window, warnings);
        var latency = MeasureLatency(export, window, warnings);
        var backlog = MeasureBacklog(export, window, generator);
        var outcomes = CountOutcomes(export, window);
        var deadLetters = ReconcileDeadLetters(configuration, export, window, outcomes);
        var reconciliation = Reconcile(generator, outcomes, deadLetters, backlog);

        if (generator.Failed > 0)
            warnings.Add(Inv($"The generator reported {generator.Failed:N0} failed sends. They are not in its sent count and so not in the reconciliation."));

        var measurementWindow = new MeasurementWindow(
            window.Start, window.End, window.Duration.TotalSeconds, window.ExpectedBins, throughput.BinsWithData, (window.RunEnd - window.RunStart).TotalSeconds);
        var verdictCriteria = new List<CriterionResult>
        {
            ThroughputResult(throughput.Figures, measurementWindow, Criteria),
            BacklogResult(backlog),
            LatencyResult(latency, Criteria),
            LostResult(reconciliation, backlogHasData: export.Backlog.Count > 0),
            DeadLetterResult(deadLetters),
            generator.HeldTargetRate
                ? Result(GeneratorCriterion, true, $"Generator achieved {generator.AchievedRate:0.#} msg/s against a target of {generator.TargetRate:0.#}.")
                : Result(GeneratorCriterion, false, $"The generator achieved {generator.AchievedRate:0.#} msg/s against a target of {generator.TargetRate:0.#}, or had send failures; this run did not apply the intended load."),
        };

        return new RunRecord
        {
            Id = MakeId(configuration, window.RunStart),
            RecordedAtUtc = recordedAtUtc,
            Configuration = configuration with { StartUtc = window.RunStart, EndUtc = window.RunEnd },
            Generator = new GeneratorFigures(generator.Sent, generator.Failed, generator.TargetRate, generator.AchievedRate, generator.HeldTargetRate),
            Window = measurementWindow,
            Throughput = throughput.Figures,
            Backlog = backlog,
            Latency = latency,
            Outcomes = outcomes,
            DeadLetters = deadLetters,
            Reconciliation = reconciliation,
            Verdict = new Verdict(MeetsAll(verdictCriteria), verdictCriteria, warnings),
        };
    }

    /// <summary>The run's own span, and the whole-minute measurement window inside it (edge minutes trimmed).</summary>
    private sealed record RunWindow(
        DateTimeOffset RunStart, DateTimeOffset RunEnd, DateTimeOffset FirstBin,
        DateTimeOffset Start, DateTimeOffset End, TimeSpan Duration, TimeSpan Bin, int ExpectedBins)
    {
        public bool Contains(DateTimeOffset t) => t >= Start && t < End;
    }

    private static RunWindow ComputeWindow(RunConfiguration configuration, GeneratorSummary generator, int binSeconds)
    {
        var bin = TimeSpan.FromSeconds(binSeconds);
        var runStart = configuration.StartUtc ?? generator.StartedAtUtc;
        var runEnd = configuration.EndUtc ?? generator.EndedAtUtc;
        var start = Ceiling(runStart, bin) + bin * Criteria.EdgeBinsToTrim;
        var end = Floor(runEnd, bin) - bin * Criteria.EdgeBinsToTrim;
        var duration = end > start ? end - start : TimeSpan.Zero;
        return new RunWindow(runStart, runEnd, Floor(runStart, bin), start, end, duration, bin, (int)(duration / bin));
    }

    private sealed record MeasuredThroughput(ThroughputFigures Figures, int BinsWithData);

    /// <summary>Sustained throughput: outputs per second over the whole minutes inside the run.</summary>
    private static MeasuredThroughput MeasureThroughput(MetricsExport export, RunWindow window, List<string> warnings)
    {
        var windowOutcomes = export.Outcomes.Where(o => window.Contains(o.Timestamp)).ToList();
        var binsWithData = windowOutcomes.Select(o => o.Timestamp.UtcTicks).Distinct().Count();
        var outputsByBin = windowOutcomes
            .Where(o => MessageOutcomes.IsOutput(o.Outcome))
            .GroupBy(o => o.Timestamp.UtcTicks)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Count));
        var perBinTps = Enumerable.Range(0, window.ExpectedBins)
            .Select(i => outputsByBin.GetValueOrDefault((window.Start + window.Bin * i).UtcTicks) / window.Bin.TotalSeconds)
            .ToList();
        var outputsInWindow = outputsByBin.Values.Sum();
        var sustainedTps = window.Duration > TimeSpan.Zero ? outputsInWindow / window.Duration.TotalSeconds : 0;
        if (binsWithData < window.ExpectedBins)
            warnings.Add(Inv($"Only {binsWithData} of {window.ExpectedBins} minutes in the measurement window have outcome data; the missing minutes count as zero throughput."));

        return new MeasuredThroughput(
            new ThroughputFigures(
                outputsInWindow, sustainedTps,
                perBinTps.Count > 0 ? perBinTps.Min() : 0, perBinTps.Count > 0 ? perBinTps.Max() : 0, perBinTps),
            binsWithData);
    }

    /// <summary>Latency percentiles from the histogram buckets, window only.</summary>
    private static LatencyFigures MeasureLatency(MetricsExport export, RunWindow window, List<string> warnings)
    {
        var histogram = new LatencyHistogram(export.LatencyBuckets
            .Where(b => window.Contains(b.Timestamp))
            .Select(b => (b.Le, b.Count)));
        var p50 = histogram.Percentile(0.50);
        var p95 = histogram.Percentile(0.95);
        var p99 = histogram.Percentile(0.99);
        if (histogram.Total == 0) warnings.Add("No latency samples fall inside the measurement window.");
        return new LatencyFigures(
            histogram.Total, p50?.Estimate, p95?.Estimate, p99?.Estimate, p50?.UpperBound, p95?.UpperBound, p99?.UpperBound);
    }

    /// <summary>Backlog trend over the window, plus where the backlog stood at the end of the data.</summary>
    private static BacklogFigures MeasureBacklog(MetricsExport export, RunWindow window, GeneratorSummary generator)
    {
        var allBacklog = export.Backlog.OrderBy(b => b.Timestamp).ToList();
        var windowBacklog = allBacklog.Where(b => window.Contains(b.Timestamp)).ToList();
        var growthThreshold = Criteria.BacklogGrowthSecondsOfLoad * generator.TargetRate;
        return BacklogTrend(windowBacklog, window.Duration, growthThreshold, allBacklog.Count > 0 ? allBacklog[^1].ActiveMessages : null);
    }

    /// <summary>Outcome counts over the whole run (from the first minute of publishing to the end of the data).</summary>
    private static OutcomeFigures CountOutcomes(MetricsExport export, RunWindow window)
    {
        var runOutcomes = export.Outcomes.Where(o => o.Timestamp >= window.FirstBin).ToList();
        long Total(string outcome) => runOutcomes.Where(o => o.Outcome == outcome).Sum(o => o.Count);
        IReadOnlyDictionary<string, long> ByReason(string outcome) => runOutcomes
            .Where(o => o.Outcome == outcome)
            .GroupBy(o => string.IsNullOrWhiteSpace(o.Reason) ? "unknown" : o.Reason!)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(o => o.Count));
        var deadLetteredByReason = ByReason(MessageOutcomes.DeadLettered);
        var abandonedByReason = ByReason(MessageOutcomes.Abandoned);
        return new OutcomeFigures(
            Total(MessageOutcomes.SentToQueue), Total(MessageOutcomes.SentToEventHub),
            deadLetteredByReason.Values.Sum(), abandonedByReason.Values.Sum(), deadLetteredByReason, abandonedByReason);
    }

    /// <summary>Dead-letters: what the host counted against what Service Bus holds.</summary>
    private static DeadLetterFigures ReconcileDeadLetters(RunConfiguration configuration, MetricsExport export, RunWindow window, OutcomeFigures outcomes)
    {
        var expectedReasons = configuration.ExpectedDeadLetterReasons.ToHashSet(StringComparer.Ordinal);
        var unexpected = outcomes.DeadLetteredByReason.Where(kv => !expectedReasons.Contains(kv.Key)).Sum(kv => kv.Value);
        var dlq = export.DeadLetterQueue.OrderBy(d => d.Timestamp).ToList();
        long serverGrowth = 0, serverUnaccounted = 0;
        if (dlq.Count > 0)
        {
            // Baseline: the last sample before the run (the queue may not have been empty), else the first one.
            var before = dlq.LastOrDefault(d => d.Timestamp < window.FirstBin) ?? dlq[0];
            serverGrowth = (long)Math.Max(0, Math.Round(dlq[^1].Messages - before.Messages));
            serverUnaccounted = Math.Max(0, serverGrowth - outcomes.DeadLettered);
        }
        return new DeadLetterFigures(outcomes.DeadLettered, unexpected, dlq.Count > 0, serverGrowth, serverUnaccounted, unexpected + serverUnaccounted);
    }

    /// <summary>Reconciliation against the generator's own count: every message sent is an output, a dead-letter or still in the backlog.</summary>
    private static ReconciliationFigures Reconcile(
        GeneratorSummary generator, OutcomeFigures outcomes, DeadLetterFigures deadLetters, BacklogFigures backlog)
    {
        var stillInBacklog = (long)Math.Round(backlog.AtEndOfData ?? 0);
        var accounted = outcomes.SentToQueue + outcomes.SentToEventHub + outcomes.DeadLettered + deadLetters.ServerSideUnaccounted;
        return new ReconciliationFigures(
            generator.Sent, outcomes.SentToQueue, outcomes.SentToEventHub, outcomes.DeadLettered, deadLetters.ServerSideUnaccounted, stillInBacklog, accounted,
            Lost: Math.Max(0, generator.Sent - accounted - stillInBacklog),
            Duplicates: Math.Max(0, accounted - generator.Sent));
    }

    private static BacklogFigures BacklogTrend(List<BacklogSample> window, TimeSpan windowDuration, double growthThreshold, double? atEndOfData)
    {
        if (window.Count < 2)
            return new BacklogFigures(false, window.FirstOrDefault()?.ActiveMessages, window.LastOrDefault()?.ActiveMessages,
                window.Count > 0 ? window.Max(b => b.ActiveMessages) : null, 0, 0, growthThreshold, false, atEndOfData);

        // Least-squares slope of backlog against time: robust to a spike or a noisy sample, unlike last minus first.
        var t0 = window[0].Timestamp;
        var xs = window.Select(b => (b.Timestamp - t0).TotalSeconds).ToArray();
        var ys = window.Select(b => b.ActiveMessages).ToArray();
        var meanX = xs.Average();
        var meanY = ys.Average();
        var slope = xs.Zip(ys, (x, y) => (x - meanX) * (y - meanY)).Sum() / xs.Sum(x => (x - meanX) * (x - meanX));
        var growth = slope * windowDuration.TotalSeconds;

        return new BacklogFigures(true, ys[0], ys[^1], ys.Max(), slope, growth, growthThreshold, growth > growthThreshold, atEndOfData);
    }

    private static CriterionResult ThroughputResult(ThroughputFigures t, MeasurementWindow w, GoNoGoCriteria c)
    {
        var requiredTps = c.RequiredTps * (1 - c.TpsTolerance);
        // Stated next to every throughput verdict so the tolerance is never hidden: the bar is 294, not 300.
        var tolerance = Inv($"The bar is {requiredTps:0.#} TPS: {c.RequiredTps:0} TPS less a {c.TpsTolerance * 100:0.#}% tolerance.");
        var problems = new List<string>();
        if (w.DurationSeconds < c.RequiredWindow.TotalSeconds)
            problems.Add(Inv($"the measurement window is {w.DurationSeconds:0} s of whole minutes inside the run, less than the {c.RequiredWindow.TotalMinutes:0} minutes needed (run the generator for at least {c.RequiredWindow.TotalMinutes + 2:0} minutes)"));
        if (t.SustainedTps < requiredTps)
            problems.Add(Inv($"sustained {t.SustainedTps:0.#} TPS is below {requiredTps:0.#}"));

        return problems.Count == 0
            ? Result(ThroughputCriterion, true, $"Sustained {t.SustainedTps:0.#} TPS over {w.DurationSeconds:0} s (lowest minute {t.MinBinTps:0.#} TPS). {tolerance}")
            : Result(ThroughputCriterion, false, $"Not shown: {string.Join("; ", problems)}. {tolerance}");
    }

    private static CriterionResult BacklogResult(BacklogFigures b)
    {
        if (!b.HasData) return Result(BacklogCriterion, false, $"No backlog samples (ActiveMessages) fall inside the measurement window, so the trend cannot be judged.");
        return b.Grew
            ? Result(BacklogCriterion, false, $"The backlog grew by about {b.GrowthOverWindow:N0} messages over the window ({b.SlopePerSecond:0.#}/s); the limit is {b.GrowthThreshold:N0}.")
            : Result(BacklogCriterion, true, $"The backlog trend is {b.SlopePerSecond:0.##}/s ({b.GrowthOverWindow:N0} over the window, limit {b.GrowthThreshold:N0}); peak {b.Max:N0}.");
    }

    private static CriterionResult LatencyResult(LatencyFigures l, GoNoGoCriteria c)
    {
        if (l.SampleCount == 0) return Result(LatencyCriterion, false, $"No latency samples, so p95 cannot be judged.");
        var passed = l.P95UpperBoundSeconds is { } upper && upper < c.MaxP95Seconds;
        var bound = l.P95UpperBoundSeconds is { } u ? Inv($"at most {u:0.###} s") : "above the largest bucket";
        return Result(LatencyCriterion, passed,
            $"p95 is about {l.P95Seconds:0.###} s ({bound}); the limit is {c.MaxP95Seconds:0.#} s. The verdict uses the bucket's upper bound.");
    }

    private static CriterionResult LostResult(ReconciliationFigures r, bool backlogHasData)
    {
        var summary = Inv($"Generator sent {r.GeneratorSent:N0}; outputs {r.SentToQueue + r.SentToEventHub:N0} (queue {r.SentToQueue:N0}, event hub {r.SentToEventHub:N0}), dead-lettered {r.DeadLettered + r.DeadLetteredByServiceBus:N0}");
        if (r.Lost > 0) return Result(LostCriterion, false, $"{r.Lost:N0} messages are lost: {summary}, still in the backlog {r.StillInBacklog:N0}.");
        if (!backlogHasData) return Result(LostCriterion, false, $"No backlog samples, so it cannot be confirmed that the run drained. {summary}.");
        if (r.StillInBacklog > 0) return Result(LostCriterion, false, $"{r.StillInBacklog:N0} messages are still in the backlog at the end of the data. Wait for the subscription to drain and export again. {summary}.");
        var duplicates = r.Duplicates > 0 ? Inv($"; {r.Duplicates:N0} more outputs than messages sent (duplicates from at-least-once delivery)") : "";
        return Result(LostCriterion, true, $"{summary}; none lost{duplicates}.");
    }

    private static CriterionResult DeadLetterResult(DeadLetterFigures d)
    {
        if (!d.ServerSideDataAvailable)
            return Result(DeadLetterCriterion, false, $"No dead-letter queue series (DeadletteredMessages), so dead-letters by delivery-count exhaustion cannot be ruled out.");
        if (d.Unexplained == 0 && d.CountedByHost == 0)
            return Result(DeadLetterCriterion, true, $"No dead-letters.");
        if (d.Unexplained == 0)
            return Result(DeadLetterCriterion, true, $"{d.CountedByHost:N0} dead-letters, all for expected reasons.");
        return Result(DeadLetterCriterion, false,
                $"{d.Unexplained:N0} unexplained: {d.UnexpectedReasons:N0} counted by the host for reasons this run did not expect, {d.ServerSideUnaccounted:N0} added to the dead-letter queue that the host did not count (most likely maximum delivery count exceeded).");
    }

    private static DateTimeOffset Floor(DateTimeOffset t, TimeSpan bin) => new(t.UtcTicks - t.UtcTicks % bin.Ticks, TimeSpan.Zero);

    private static DateTimeOffset Ceiling(DateTimeOffset t, TimeSpan bin)
    {
        var floor = Floor(t, bin);
        return floor == t.ToUniversalTime() ? floor : floor + bin;
    }

    /// <summary>A file-name-safe identifier that carries the headline configuration, for example <c>20261117-1000-baseline-300tps-3i-c16-GP_Gen5_4</c>.</summary>
    public static string MakeId(RunConfiguration c, DateTimeOffset start)
    {
        var parts = new[]
        {
            start.UtcDateTime.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture),
            c.Name,
            string.Create(CultureInfo.InvariantCulture, $"{c.TargetPublishRate:0.##}tps"),
            $"{c.InstanceCount}i",
            $"c{c.MaxConcurrentCalls}",
            c.DatabaseTier,
        };
        return Regex.Replace(string.Join('-', parts.Where(p => !string.IsNullOrWhiteSpace(p))), @"[^A-Za-z0-9_.\-]+", "_");
    }
}
