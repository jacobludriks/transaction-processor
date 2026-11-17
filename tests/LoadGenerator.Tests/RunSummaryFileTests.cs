using System.Text.Json;

namespace LoadGenerator.Tests;

/// <summary>The machine-readable run summary the generator writes for the measurement report.</summary>
public sealed class RunSummaryFileTests
{
    private static readonly DateTimeOffset Started = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    private static JsonElement Summarise(string[] args, RunSummary summary)
    {
        var options = CliOptions.Parse(args);
        return JsonDocument.Parse(RunSummaryFile.ToJson(options, summary, Started)).RootElement;
    }

    [Fact]
    public void Reports_what_was_sent_and_the_rate_achieved()
    {
        var json = Summarise(["load", "--dry-run", "--rate", "300", "--duration", "600"],
            new RunSummary(300, TimeSpan.FromSeconds(600), Sent: 179_400, Failed: 0, FirstError: null));

        Assert.Equal(179_400, json.GetProperty("sent").GetInt64());
        Assert.Equal(0, json.GetProperty("failed").GetInt64());
        Assert.Equal(299, json.GetProperty("achievedRate").GetDouble(), precision: 6);
        Assert.Equal(300, json.GetProperty("targetRate").GetDouble());
        Assert.Equal(600, json.GetProperty("elapsedSeconds").GetDouble());
    }

    [Fact]
    public void Reports_the_start_and_end_time_in_utc()
    {
        var json = Summarise(["load", "--dry-run"], new RunSummary(300, TimeSpan.FromSeconds(90), 100, 0, null));

        Assert.Equal(Started, json.GetProperty("startedAtUtc").GetDateTimeOffset());
        Assert.Equal(Started.AddSeconds(90), json.GetProperty("endedAtUtc").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(294, 0, true)]
    [InlineData(293, 0, false)]
    [InlineData(300, 5, false)]
    public void Says_whether_the_target_rate_was_held_using_the_same_rule_as_the_console_warning(double achievedPerSecond, long failed, bool held)
    {
        var json = Summarise(["load", "--dry-run", "--rate", "300"],
            new RunSummary(300, TimeSpan.FromSeconds(100), (long)(achievedPerSecond * 100), failed, failed > 0 ? "boom" : null));

        Assert.Equal(held, json.GetProperty("heldTargetRate").GetBoolean());
    }

    [Fact]
    public void Carries_the_load_settings_and_the_first_error_so_a_run_can_be_reproduced()
    {
        var json = Summarise(
            ["load", "--namespace", "x.servicebus.windows.net", "--topic", "t1", "--senders", "8", "--batch-size", "50", "--seed", "7", "--unmatched-ratio", "0.2"],
            new RunSummary(300, TimeSpan.FromSeconds(10), 10, 1, "Service Bus is busy"));

        Assert.Equal("t1", json.GetProperty("topic").GetString());
        Assert.Equal(8, json.GetProperty("senders").GetInt32());
        Assert.Equal(50, json.GetProperty("batchSize").GetInt32());
        Assert.Equal(7, json.GetProperty("seed").GetInt32());
        Assert.Equal(0.2, json.GetProperty("unmatchedRatio").GetDouble());
        Assert.False(json.GetProperty("dryRun").GetBoolean());
        Assert.Equal("Service Bus is busy", json.GetProperty("firstError").GetString());
    }

    [Fact]
    public void The_summary_file_path_is_an_option()
    {
        Assert.Equal("run.json", CliOptions.Parse(["load", "--dry-run", "--summary-file", "run.json"]).SummaryFile);
        Assert.Null(CliOptions.Parse(["load", "--dry-run"]).SummaryFile);
    }
}
