using System.Globalization;
using System.Text;

using static RunReport.InvariantText;

namespace RunReport;

/// <summary>The human-readable summary of one run.</summary>
public static class RunMarkdown
{

    internal static string Seconds(double? value) => value is { } v ? Inv($"{v:0.000} s") : "n/a";

    /// <summary>A percentile for display: its estimate, or "&gt; largest bucket" when it is in the overflow bucket.</summary>
    internal static string Percentile(double? estimate, double? upperBound) =>
        estimate is null ? "n/a" : upperBound is null ? Inv($"> {estimate:0.000} s") : Seconds(estimate);

    public static string Render(RunRecord r)
    {
        var c = r.Configuration;
        var sb = new StringBuilder();
        void Line(string text = "") => sb.Append(text).Append('\n');
        void Row(string label, string value) => Line($"| {label} | {value} |");

        Line($"# Run {r.Id}");
        Line();
        Line(r.Verdict.MeetsGoCriteria
            ? "**Verdict: this run MEETS the go criteria** (300 TPS sustained for 10 minutes, p95 under 2 s, zero lost messages, no unexplained dead-letters)."
            : "**Verdict: this run DOES NOT meet the go criteria.** See the failed criteria below.");
        Line();
        // The result comes first in each row so that a failure stands out.
        Line("| Result | Criterion | Detail |");
        Line("|---|---|---|");
        foreach (var k in r.Verdict.Criteria)
            Line($"| {(k.Passed ? "PASS" : "FAIL")} | {k.Name} | {k.Detail.Replace("|", "/")} |");
        Line();

        if (r.Verdict.Warnings.Count > 0)
        {
            Line("## Warnings");
            Line();
            foreach (var w in r.Verdict.Warnings) Line($"- {w}");
            Line();
        }

        Line("## Configuration");
        Line();
        Line("| Setting | Value |");
        Line("|---|---|");
        Row("Name", c.Name ?? "(none)");
        Row("Instances", Inv($"{c.InstanceCount}"));
        Row("Max concurrent calls", Inv($"{c.MaxConcurrentCalls}"));
        if (c.PrefetchCount is { } prefetch) Row("Prefetch count", Inv($"{prefetch}"));
        Row("Database tier", c.DatabaseTier);
        Row("Target publish rate", Inv($"{c.TargetPublishRate:0.##} msg/s"));
        Row("Run start (UTC)", Inv($"{c.StartUtc:u}"));
        Row("Run end (UTC)", Inv($"{c.EndUtc:u}"));
        if (c.ExpectedDeadLetterReasons.Count > 0) Row("Expected dead-letter reasons", string.Join(", ", c.ExpectedDeadLetterReasons));
        if (!string.IsNullOrWhiteSpace(c.Notes)) Row("Notes", c.Notes);
        Line();

        Line("## Results");
        Line();
        Line("| Measure | Value |");
        Line("|---|---|");
        Row("Measurement window", Inv($"{r.Window.DurationSeconds:0} s ({r.Window.BinsWithData} of {r.Window.ExpectedBins} whole minutes with data, from {r.Window.StartUtc:HH:mm} UTC)"));
        Row("Sustained throughput", Inv($"{r.Throughput.SustainedTps:0.0} TPS"));
        Row("Lowest / highest minute", Inv($"{r.Throughput.MinBinTps:0.0} / {r.Throughput.MaxBinTps:0.0} TPS"));
        Row("Latency p50", Percentile(r.Latency.P50Seconds, r.Latency.P50UpperBoundSeconds));
        Row("Latency p95", Percentile(r.Latency.P95Seconds, r.Latency.P95UpperBoundSeconds));
        Row("Latency p99", Percentile(r.Latency.P99Seconds, r.Latency.P99UpperBoundSeconds));
        Row("Latency samples", Inv($"{r.Latency.SampleCount:N0}"));
        Row("Backlog trend", r.Backlog.HasData
            ? Inv($"{(r.Backlog.Grew ? "GROWING" : "not growing")}: {r.Backlog.SlopePerSecond:0.##}/s, {r.Backlog.GrowthOverWindow:N0} over the window (start {r.Backlog.AtWindowStart:N0}, end {r.Backlog.AtWindowEnd:N0}, peak {r.Backlog.Max:N0})")
            : "no data");
        Line();

        Line("## Outcomes (whole run)");
        Line();
        Line("| Outcome | Count |");
        Line("|---|---|");
        Row("Sent to queue", Inv($"{r.Outcomes.SentToQueue:N0}"));
        Row("Sent to event hub", Inv($"{r.Outcomes.SentToEventHub:N0}"));
        Row("Dead-lettered (counted by the host)", Inv($"{r.Outcomes.DeadLettered:N0}"));
        Row("Abandoned for redelivery (errors)", Inv($"{r.Outcomes.Abandoned:N0}"));
        Line();

        Line("### Dead-letters by reason");
        Line();
        if (r.Outcomes.DeadLetteredByReason.Count == 0 && r.DeadLetters.ServerSideUnaccounted == 0) Line("None.");
        else
        {
            Line("| Reason | Count |");
            Line("|---|---|");
            foreach (var (reason, count) in r.Outcomes.DeadLetteredByReason) Row(reason, Inv($"{count:N0}"));
            if (r.DeadLetters.ServerSideUnaccounted > 0)
                Row("(added by Service Bus, not counted by the host: usually MaxDeliveryCountExceeded)", Inv($"{r.DeadLetters.ServerSideUnaccounted:N0}"));
        }
        Line();

        if (r.Outcomes.AbandonedByReason.Count > 0)
        {
            Line("### Abandoned by reason");
            Line();
            Line("| Reason | Count |");
            Line("|---|---|");
            foreach (var (reason, count) in r.Outcomes.AbandonedByReason) Row(reason, Inv($"{count:N0}"));
            Line();
        }

        var rec = r.Reconciliation;
        Line("## Reconciliation: generator sent against outputs");
        Line();
        Line("| Item | Messages |");
        Line("|---|---|");
        Row("Generator sent", Inv($"{rec.GeneratorSent:N0}"));
        Row("Sent to queue", Inv($"{rec.SentToQueue:N0}"));
        Row("Sent to event hub", Inv($"{rec.SentToEventHub:N0}"));
        Row("Dead-lettered", Inv($"{rec.DeadLettered + rec.DeadLetteredByServiceBus:N0}"));
        Row("Still in the backlog at the end of the data", Inv($"{rec.StillInBacklog:N0}"));
        Row("Lost", Inv($"{rec.Lost:N0}"));
        Row("Duplicates (more outputs than sent)", Inv($"{rec.Duplicates:N0}"));
        Line();

        Line("## Throughput per minute (TPS)");
        Line();
        Line(string.Join(", ", r.Throughput.PerBinTps.Select(t => Inv($"{t:0.0}"))));
        Line();
        Line($"Recorded {Inv($"{r.RecordedAtUtc:u}")}.");
        return sb.ToString();
    }
}
