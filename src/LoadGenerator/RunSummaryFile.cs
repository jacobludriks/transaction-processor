using System.Text.Json;

namespace LoadGenerator;

/// <summary>
/// The machine-readable summary of a load run, written with <c>--summary-file</c>. The measurement report reads
/// <c>sent</c> (to reconcile against the outputs the processor produced), <c>startedAtUtc</c> and
/// <c>endedAtUtc</c> (the run window) and <c>targetRate</c>.
/// </summary>
public static class RunSummaryFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string ToJson(CliOptions options, RunSummary summary, DateTimeOffset startedAtUtc) =>
        JsonSerializer.Serialize(new
        {
            startedAtUtc = startedAtUtc.ToUniversalTime(),
            endedAtUtc = (startedAtUtc + summary.Elapsed).ToUniversalTime(),
            elapsedSeconds = summary.Elapsed.TotalSeconds,
            targetRate = summary.TargetRate,
            sent = summary.Sent,
            failed = summary.Failed,
            achievedRate = summary.AchievedRate,
            heldTargetRate = summary.HeldTargetRate,
            firstError = summary.FirstError,
            unmatchedRatio = options.UnmatchedRatio,
            senders = options.Senders,
            batchSize = options.BatchSize,
            seed = options.Seed,
            topic = options.Topic,
            dryRun = options.DryRun,
        }, Options);
}
