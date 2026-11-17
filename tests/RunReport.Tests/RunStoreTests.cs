using System.Text.Json;

namespace RunReport.Tests;

/// <summary>A run is saved as JSON plus a readable markdown summary, with its configuration, and can be loaded again.</summary>
public sealed class RunStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "runreport-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private static RunRecord Record(SyntheticRun run, Func<RunConfiguration, RunConfiguration>? adjust = null) =>
        RunAnalyzer.Analyze(run.Configuration(adjust), run.Export(), run.Generator(),
            new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Saving_a_run_writes_a_json_record_and_a_markdown_summary_named_after_the_run()
    {
        var record = Record(new SyntheticRun());

        var saved = RunStore.Save(record, _dir);

        Assert.Equal(Path.Combine(_dir, record.Id + ".json"), saved.JsonPath);
        Assert.Equal(Path.Combine(_dir, record.Id + ".md"), saved.MarkdownPath);
        Assert.True(File.Exists(saved.JsonPath));
        Assert.True(File.Exists(saved.MarkdownPath));
    }

    [Fact]
    public void The_json_record_carries_the_configuration_and_results_in_camel_case()
    {
        var saved = RunStore.Save(Record(new SyntheticRun(), c => c with { InstanceCount = 5, MaxConcurrentCalls = 64, DatabaseTier = "GP_Gen5_8" }), _dir);

        var root = JsonDocument.Parse(File.ReadAllText(saved.JsonPath)).RootElement;
        var configuration = root.GetProperty("configuration");
        Assert.Equal(5, configuration.GetProperty("instanceCount").GetInt32());
        Assert.Equal(64, configuration.GetProperty("maxConcurrentCalls").GetInt32());
        Assert.Equal("GP_Gen5_8", configuration.GetProperty("databaseTier").GetString());
        Assert.Equal(300, configuration.GetProperty("targetPublishRate").GetDouble());
        Assert.Equal(300, root.GetProperty("throughput").GetProperty("sustainedTps").GetDouble(), precision: 6);
        Assert.True(root.GetProperty("verdict").GetProperty("meetsGoCriteria").GetBoolean());
    }

    [Fact]
    public void A_saved_run_loads_back_with_the_same_figures()
    {
        var record = Record(new SyntheticRun { OutputsMissing = 25 });
        var saved = RunStore.Save(record, _dir);

        var loaded = RunStore.Load(saved.JsonPath);

        Assert.Equal(record.Id, loaded.Id);
        Assert.Equal(
            JsonSerializer.Serialize(record.Configuration, RunStore.JsonOptions),
            JsonSerializer.Serialize(loaded.Configuration, RunStore.JsonOptions));
        Assert.Equal(record.Throughput.SustainedTps, loaded.Throughput.SustainedTps);
        Assert.Equal(record.Latency, loaded.Latency);
        Assert.Equal(25, loaded.Reconciliation.Lost);
        Assert.False(loaded.Verdict.MeetsGoCriteria);
        Assert.Equal(record.Verdict.Criteria.Select(c => (c.Name, c.Passed)), loaded.Verdict.Criteria.Select(c => (c.Name, c.Passed)));
    }

    [Fact]
    public void A_percentile_in_the_overflow_bucket_survives_the_json_round_trip_as_unbounded()
    {
        var record = Record(new SyntheticRun { LatencyShares = [0.10, 0.40, 0.30, 0.10, 0.03, 0.02, 0.05] });

        var loaded = RunStore.Load(RunStore.Save(record, _dir).JsonPath);

        Assert.Null(loaded.Latency.P99UpperBoundSeconds);
    }

    [Fact]
    public void The_markdown_summary_states_the_verdict_the_configuration_and_the_headline_numbers()
    {
        var markdown = File.ReadAllText(RunStore.Save(Record(new SyntheticRun(), c => c with { InstanceCount = 5, MaxConcurrentCalls = 64, DatabaseTier = "GP_Gen5_8" }), _dir).MarkdownPath);

        Assert.Contains("MEETS the go criteria", markdown);
        Assert.Contains("GP_Gen5_8", markdown);
        Assert.Contains("| Instances | 5 |", markdown);
        Assert.Contains("| Max concurrent calls | 64 |", markdown);
        Assert.Contains("| Target publish rate | 300 msg/s |", markdown);
        Assert.Contains("| Sustained throughput | 300.0 TPS |", markdown);
        Assert.Contains("2% tolerance", markdown);
        Assert.Contains("| Latency p50 | 0.250 s |", markdown);
        Assert.Contains("| Latency p95 | 1.000 s |", markdown);
        Assert.Contains("| Latency p99 | 2.000 s |", markdown);
    }

    [Fact]
    public void The_markdown_summary_of_a_failing_run_says_which_criteria_failed_and_why()
    {
        var markdown = File.ReadAllText(RunStore.Save(Record(new SyntheticRun { OutputsMissing = 25 }), _dir).MarkdownPath);

        Assert.Contains("DOES NOT meet the go criteria", markdown);
        Assert.Contains("| FAIL | Zero lost messages |", markdown);
        Assert.Contains("| PASS | Sustained throughput |", markdown);
        Assert.Contains("25 messages are lost", markdown);
    }

    [Fact]
    public void The_markdown_summary_lists_dead_letters_by_reason_and_the_reconciliation()
    {
        var run = new SyntheticRun { OutputsMissing = 9, DeadLetterQueue = t => t >= new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero) ? 9 : 0 };
        var minute = new DateTimeOffset(2026, 11, 17, 10, 5, 0, TimeSpan.Zero);
        run.ExtraOutcomes.Add(new OutcomeSample(minute, "dead_lettered", "ItemNotFound", 7));
        run.ExtraOutcomes.Add(new OutcomeSample(minute, "dead_lettered", "InvalidXml", 2));

        var markdown = File.ReadAllText(RunStore.Save(Record(run), _dir).MarkdownPath);

        Assert.Contains("| ItemNotFound | 7 |", markdown);
        Assert.Contains("| InvalidXml | 2 |", markdown);
        Assert.Contains("| Generator sent | 252,000 |", markdown);
        Assert.Contains("| Lost | 0 |", markdown);
    }

    [Fact]
    public void Loading_a_directory_returns_its_runs_oldest_first()
    {
        var later = new SyntheticRun { Start = new DateTimeOffset(2026, 11, 17, 14, 0, 20, TimeSpan.Zero) };
        var earlier = new SyntheticRun();
        RunStore.Save(Record(later), _dir);
        RunStore.Save(Record(earlier), _dir);

        var runs = RunStore.LoadAll(_dir);

        Assert.Equal(2, runs.Count);
        Assert.True(runs[0].Configuration.StartUtc < runs[1].Configuration.StartUtc);
    }
}
