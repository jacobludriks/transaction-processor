using System.Globalization;
using System.Text;

using static RunReport.InvariantText;

namespace RunReport;

/// <summary>Several saved runs as one markdown table, oldest first, so configurations and results can be compared side by side.</summary>
public static class RunComparison
{

    public static string ToMarkdown(IEnumerable<RunRecord> runs)
    {
        var sb = new StringBuilder();
        sb.Append("| Run | Instances | Max calls | Database | Target | Sustained TPS | p50 | p95 | p99 | Backlog grew | Dead-lettered | Lost | Meets go criteria |\n");
        sb.Append("|---|---|---|---|---|---|---|---|---|---|---|---|---|\n");
        foreach (var r in runs.OrderBy(r => r.Configuration.StartUtc).ThenBy(r => r.Id, StringComparer.Ordinal))
        {
            var c = r.Configuration;
            var deadLettered = r.Reconciliation.DeadLettered + r.Reconciliation.DeadLetteredByServiceBus;
            sb.Append(Inv($"| {r.Id} | {c.InstanceCount} | {c.MaxConcurrentCalls} | {c.DatabaseTier} | {c.TargetPublishRate:0.##} | {r.Throughput.SustainedTps:0.0} | "))
              .Append(RunMarkdown.Percentile(r.Latency.P50Seconds, r.Latency.P50UpperBoundSeconds)).Append(" | ")
              .Append(RunMarkdown.Percentile(r.Latency.P95Seconds, r.Latency.P95UpperBoundSeconds)).Append(" | ")
              .Append(RunMarkdown.Percentile(r.Latency.P99Seconds, r.Latency.P99UpperBoundSeconds)).Append(" | ")
              .Append(r.Backlog.HasData ? (r.Backlog.Grew ? "yes" : "no") : "n/a")
              .Append(Inv($" | {deadLettered} | {r.Reconciliation.Lost} | "))
              .Append(r.Verdict.MeetsGoCriteria ? "yes" : "no")
              .Append(" |\n");
        }
        return sb.ToString();
    }
}
