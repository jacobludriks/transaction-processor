using System.Text.Json;

namespace RunReport.Tests;

/// <summary>The command line: the three input files in, a saved record and a printed summary out.</summary>
public sealed class CliTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "runreport-cli-" + Guid.NewGuid().ToString("N"));

    public CliTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // The run configuration as an operator writes it by hand. No start or end: the generator summary supplies them.
    private const string ConfigJson = """
        {
          "name": "baseline",
          "instanceCount": 3,
          "maxConcurrentCalls": 16,
          "prefetchCount": 32,
          "databaseTier": "GP_Gen5_4",
          "targetPublishRate": 300
        }
        """;

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // A generator summary in the shape the load generator's --summary-file writes (extra fields are ignored).
    private static string GeneratorJson(SyntheticRun run) => $$"""
        {
          "startedAtUtc": "{{run.Start:O}}",
          "endedAtUtc": "{{run.End:O}}",
          "elapsedSeconds": {{run.Duration.TotalSeconds}},
          "targetRate": {{run.PublishTps}},
          "sent": {{(long)(run.PublishTps * run.Duration.TotalSeconds)}},
          "failed": 0,
          "achievedRate": {{run.PublishTps}},
          "heldTargetRate": true,
          "firstError": null,
          "unmatchedRatio": 0.1,
          "senders": 4,
          "batchSize": 100,
          "seed": 12345,
          "topic": "transactions-in",
          "dryRun": false
        }
        """;

    private (int Exit, string Out, string Err) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = Cli.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    private (int Exit, string Out, string Err) Analyze(SyntheticRun run, string outDir)
    {
        var config = Write("config.json", ConfigJson);
        var metrics = Write("metrics.json", JsonSerializer.Serialize(run.Export(), RunStore.JsonOptions));
        var generator = Write("generator.json", GeneratorJson(run));
        return Run("analyze", "--config", config, "--metrics", metrics, "--generator", generator, "--out", outDir);
    }

    [Fact]
    public void Analyze_saves_the_record_and_prints_the_summary()
    {
        var outDir = Path.Combine(_dir, "runs");

        var result = Analyze(new SyntheticRun(), outDir);

        Assert.Equal(0, result.Exit);
        Assert.Contains("MEETS the go criteria", result.Out);
        Assert.Single(Directory.GetFiles(outDir, "*.json"));
        Assert.Single(Directory.GetFiles(outDir, "*.md"));
        Assert.Contains("baseline", Path.GetFileName(Directory.GetFiles(outDir, "*.json")[0]));
    }

    [Fact]
    public void Analyze_exits_1_when_the_run_does_not_meet_the_criteria()
    {
        var result = Analyze(new SyntheticRun { OutputsMissing = 25 }, Path.Combine(_dir, "runs"));

        Assert.Equal(1, result.Exit);
        Assert.Contains("DOES NOT meet the go criteria", result.Out);
    }

    [Fact]
    public void A_missing_input_file_is_a_usage_error_that_names_the_file()
    {
        var result = Run("analyze", "--config", Path.Combine(_dir, "nope.json"), "--metrics", "a", "--generator", "b");

        Assert.Equal(2, result.Exit);
        Assert.Contains("nope.json", result.Err);
    }

    [Fact]
    public void An_input_file_that_is_not_the_expected_json_is_a_usage_error()
    {
        var config = Write("config.json", "not json");
        var result = Run("analyze", "--config", config, "--metrics", config, "--generator", config);

        Assert.Equal(2, result.Exit);
        Assert.Contains("config.json", result.Err);
    }

    [Fact]
    public void An_unknown_command_prints_the_usage()
    {
        var result = Run("frobnicate");

        Assert.Equal(2, result.Exit);
        Assert.Contains("Usage", result.Err);
    }

    [Fact]
    public void Compare_prints_a_table_of_the_saved_runs_in_a_directory_and_can_save_it()
    {
        var outDir = Path.Combine(_dir, "runs");
        Analyze(new SyntheticRun(), outDir);
        Analyze(new SyntheticRun { Start = new DateTimeOffset(2026, 11, 17, 15, 0, 20, TimeSpan.Zero), ProcessTps = 250 }, outDir);
        var saved = Path.Combine(_dir, "comparison.md");

        var result = Run("compare", outDir, "--out", saved);

        Assert.Equal(0, result.Exit);
        var rows = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).Where(l => l.StartsWith("| 2026") || l.StartsWith("| 20")).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains("| yes |", rows[0]);
        Assert.Contains("| no |", rows[1]);
        Assert.Equal(result.Out.TrimEnd(), File.ReadAllText(saved).TrimEnd());
    }

    [Fact]
    public void Compare_with_nothing_to_compare_is_a_usage_error()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);

        Assert.Equal(2, Run("compare", empty).Exit);
    }
}
