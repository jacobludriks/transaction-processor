namespace RunReport.Tests;

/// <summary>Several saved runs side by side, so configurations can be compared.</summary>
public sealed class RunComparisonTests
{
    private static RunRecord Record(SyntheticRun run, Func<RunConfiguration, RunConfiguration> adjust) =>
        RunAnalyzer.Analyze(run.Configuration(adjust), run.Export(), run.Generator(),
            new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Each_run_is_a_row_with_its_configuration_and_headline_results()
    {
        var baseline = Record(new SyntheticRun(), c => c with { Name = "baseline", InstanceCount = 3, MaxConcurrentCalls = 16, DatabaseTier = "GP_Gen5_4" });
        var bigger = Record(
            new SyntheticRun { PublishTps = 450, ProcessTps = 450, LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.04, 0.06, 0.0] },
            c => c with { Name = "peak", InstanceCount = 3, MaxConcurrentCalls = 64, DatabaseTier = "GP_Gen5_8" });

        var table = RunComparison.ToMarkdown([baseline, bigger]).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(
            "| Run | Instances | Max calls | Database | Target | Sustained TPS | p50 | p95 | p99 | Backlog grew | Dead-lettered | Lost | Meets go criteria |",
            table[0].Trim());
        Assert.StartsWith("|---", table[1]);
        Assert.Equal(
            "| " + baseline.Id + " | 3 | 16 | GP_Gen5_4 | 300 | 300.0 | 0.250 s | 1.000 s | 2.000 s | no | 0 | 0 | yes |",
            table[2].Trim());
        Assert.Equal(
            "| " + bigger.Id + " | 3 | 64 | GP_Gen5_8 | 450 | 450.0 | 0.250 s | 2.500 s | 4.500 s | no | 0 | 0 | no |",
            table[3].Trim());
    }

    [Fact]
    public void Runs_are_listed_oldest_first_whatever_order_they_are_given_in()
    {
        var early = Record(new SyntheticRun(), c => c with { Name = "early" });
        var late = Record(new SyntheticRun { Start = new DateTimeOffset(2026, 11, 17, 15, 0, 20, TimeSpan.Zero) }, c => c with { Name = "late" });

        var lines = RunComparison.ToMarkdown([late, early]).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Contains("early", lines[2]);
        Assert.Contains("late", lines[3]);
    }

    [Fact]
    public void An_unbounded_percentile_is_shown_as_over_the_largest_bucket()
    {
        var run = Record(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.03, 0.02, 0.05] }, c => c);

        var row = RunComparison.ToMarkdown([run]).Split('\n', StringSplitOptions.RemoveEmptyEntries)[2];

        Assert.Contains("> 5.000 s", row);
    }
}
