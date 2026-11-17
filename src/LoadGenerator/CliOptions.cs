using System.Globalization;

namespace LoadGenerator;

public sealed class CliOptions
{
    // The one source of truth for the defaults shared with the publisher: a default PublishOptions.
    private static readonly PublishOptions Defaults = new();
    private const string DefaultTopic = "transactions-in";

    public static readonly string Usage = string.Create(CultureInfo.InvariantCulture, $"""
        Usage:
          LoadGenerator load        [options]   publish load messages at a steady rate
          LoadGenerator functional  [options]   send the small labelled set of bad messages (once)

        Connection (not needed with --dry-run):
          --namespace <fqdn>          Service Bus namespace, e.g. mysb.servicebus.windows.net
          --topic <name>              input topic (default {DefaultTopic})
          --client-id <guid>          client ID of the managed identity to use (the tooling identity);
                                      omit to use the signed-in developer account

        Load mode:
          --rate <msgs/sec>           target rate (default {Defaults.Rate})
          --duration <seconds>        how long to run (default {Defaults.Duration.TotalSeconds})
          --unmatched-ratio <0..1>    share of messages with an unknown SKU (default {Defaults.UnmatchedRatio:0.00})
          --senders <n>               concurrent senders (default {Defaults.Senders})
          --batch-size <n>            messages per send (default {Defaults.BatchSize})
          --seed <n>                  random seed (default {Defaults.Seed})
          --summary-file <path>       write a machine-readable run summary (JSON) for the measurement report
          --dry-run                   generate and pace, but send nothing (no Azure needed)

        Functional mode:
          --include-missing-sku       also send one message with no skuId
          --include-unknown-item      also send one message whose itemId does not exist
          --dry-run                   print the messages' labels without sending
        """);

    public required string Mode { get; init; }
    public string? Namespace { get; init; }
    public string Topic { get; init; } = DefaultTopic;
    public string? ClientId { get; init; }
    public double Rate { get; init; } = Defaults.Rate;
    public double DurationSeconds { get; init; } = Defaults.Duration.TotalSeconds;
    public double UnmatchedRatio { get; init; } = Defaults.UnmatchedRatio;
    public int Senders { get; init; } = Defaults.Senders;
    public int BatchSize { get; init; } = Defaults.BatchSize;
    public int Seed { get; init; } = Defaults.Seed;
    public bool DryRun { get; init; }
    public string? SummaryFile { get; init; }
    public bool IncludeMissingSku { get; init; }
    public bool IncludeUnknownItem { get; init; }

    /// <summary>Parses the command line. Throws <see cref="ArgumentException"/> with a message for the operator on bad input.</summary>
    public static CliOptions Parse(string[] args)
    {
        if (args.Length == 0 || args[0] is not ("load" or "functional"))
            throw new ArgumentException("The first argument must be 'load' or 'functional'.");

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--dry-run" or "--include-missing-sku" or "--include-unknown-item") flags.Add(arg);
            else if (arg.StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length) values[arg] = args[++i];
            else throw new ArgumentException($"Unknown or incomplete option '{arg}'.");
        }

        var known = new HashSet<string> { "--namespace", "--topic", "--client-id", "--rate", "--duration", "--unmatched-ratio", "--senders", "--batch-size", "--seed", "--summary-file" };
        var unknown = values.Keys.FirstOrDefault(k => !known.Contains(k));
        if (unknown is not null) throw new ArgumentException($"Unknown option '{unknown}'.");

        var options = new CliOptions
        {
            Mode = args[0],
            Namespace = values.GetValueOrDefault("--namespace"),
            Topic = values.GetValueOrDefault("--topic") ?? DefaultTopic,
            ClientId = values.GetValueOrDefault("--client-id"),
            Rate = Double(values, "--rate", Defaults.Rate),
            DurationSeconds = Double(values, "--duration", Defaults.Duration.TotalSeconds),
            UnmatchedRatio = Double(values, "--unmatched-ratio", Defaults.UnmatchedRatio),
            Senders = (int)Double(values, "--senders", Defaults.Senders),
            BatchSize = (int)Double(values, "--batch-size", Defaults.BatchSize),
            Seed = (int)Double(values, "--seed", Defaults.Seed),
            DryRun = flags.Contains("--dry-run"),
            SummaryFile = values.GetValueOrDefault("--summary-file"),
            IncludeMissingSku = flags.Contains("--include-missing-sku"),
            IncludeUnknownItem = flags.Contains("--include-unknown-item"),
        };

        if (options.Rate <= 0) throw new ArgumentException("--rate must be greater than 0.");
        if (options.DurationSeconds <= 0) throw new ArgumentException("--duration must be greater than 0.");
        if (options.UnmatchedRatio is < 0 or > 1) throw new ArgumentException("--unmatched-ratio must be between 0 and 1.");
        if (options.Senders < 1) throw new ArgumentException("--senders must be at least 1.");
        if (options.BatchSize < 1) throw new ArgumentException("--batch-size must be at least 1.");
        if (!options.DryRun && string.IsNullOrWhiteSpace(options.Namespace))
            throw new ArgumentException("--namespace is required unless --dry-run is given.");
        return options;
    }

    private static double Double(Dictionary<string, string> values, string name, double fallback) =>
        values.TryGetValue(name, out var text)
            ? double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new ArgumentException($"{name} must be a number, got '{text}'.")
            : fallback;
}
