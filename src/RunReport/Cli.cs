using System.Globalization;
using System.Text.Json;

namespace RunReport;

/// <summary>The command line. Exit codes: 0 fine, 1 the analysed run does not meet the go criteria, 2 bad usage or input.</summary>
public static class Cli
{
    public const string Usage = """
        Usage:
          RunReport analyze --config <run-config.json> --metrics <metrics.json> --generator <generator-summary.json> [--out <dir>]
              Computes the run's results, saves <id>.json and <id>.md in the output directory (default ./runs),
              and prints the markdown summary. Exit code 1 if the run does not meet the go criteria.

          RunReport compare <dir-or-record.json>... [--out <file.md>]
              Prints (and optionally saves) a table of saved runs, oldest first.
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            return args.FirstOrDefault() switch
            {
                "analyze" => Analyze(args[1..], output),
                "compare" => Compare(args[1..], output),
                _ => Fail(error, "Unknown or missing command."),
            };
        }
        catch (UsageException ex)
        {
            return Fail(error, ex.Message);
        }
    }

    private static int Analyze(string[] args, TextWriter output)
    {
        var options = Options(args, "--config", "--metrics", "--generator", "--out");
        var config = ReadJson<RunConfiguration>(Required(options, "--config"));
        var metrics = ReadJson<MetricsExport>(Required(options, "--metrics"));
        var generator = ReadJson<GeneratorSummary>(Required(options, "--generator"));

        var record = RunAnalyzer.Analyze(config, metrics, generator, DateTimeOffset.UtcNow);
        var saved = RunStore.Save(record, options.GetValueOrDefault("--out") ?? "runs");

        output.Write(RunMarkdown.Render(record));
        output.WriteLine();
        output.WriteLine($"Saved {saved.JsonPath} and {saved.MarkdownPath}");
        return record.Verdict.MeetsGoCriteria ? 0 : 1;
    }

    private static int Compare(string[] args, TextWriter output)
    {
        var paths = new List<string>();
        string? outFile = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--out" && i + 1 < args.Length) outFile = args[++i];
            else if (args[i].StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"Unknown option '{args[i]}'.");
            else paths.Add(args[i]);
        }

        var runs = new List<RunRecord>();
        foreach (var path in paths)
        {
            if (Directory.Exists(path)) runs.AddRange(LoadDirectory(path));
            else runs.Add(Load(path));
        }
        if (runs.Count == 0) throw new UsageException("There are no saved runs to compare.");

        var table = RunComparison.ToMarkdown(runs);
        output.Write(table);
        if (outFile is not null) File.WriteAllText(outFile, table);
        return 0;
    }

    private static IReadOnlyList<RunRecord> LoadDirectory(string directory)
    {
        try { return RunStore.LoadAll(directory); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw new UsageException($"{directory}: {ex.Message}"); }
    }

    private static RunRecord Load(string path)
    {
        if (!File.Exists(path)) throw new UsageException($"File not found: {path}");
        try { return RunStore.Load(path); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException) { throw new UsageException($"{path} is not a saved run record: {ex.Message}"); }
    }

    private static T ReadJson<T>(string path)
    {
        if (!File.Exists(path)) throw new UsageException($"File not found: {path}");
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), RunStore.JsonOptions)
                ?? throw new UsageException($"{path} is empty.");
        }
        catch (JsonException ex)
        {
            throw new UsageException($"{path} is not valid {typeof(T).Name} JSON: {ex.Message}");
        }
    }

    private static Dictionary<string, string> Options(string[] args, params string[] allowed)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!allowed.Contains(args[i]) || i + 1 >= args.Length) throw new UsageException($"Unknown or incomplete option '{args[i]}'.");
            values[args[i]] = args[++i];
        }
        return values;
    }

    private static string Required(Dictionary<string, string> options, string name) =>
        options.GetValueOrDefault(name) ?? throw new UsageException($"{name} is required.");

    private static int Fail(TextWriter error, string message)
    {
        error.WriteLine(message);
        error.WriteLine();
        error.WriteLine(Usage);
        return 2;
    }

    private sealed class UsageException(string message) : Exception(message);
}
